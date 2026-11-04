import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  HttpError,
  HttpTransportType,
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';
import { BotLevel, createPlayer, defaultApiUrl, Profile, renamePlayer, RoomView, UnauthorizedError } from './api';

const PROFILE_KEY = 'bluff.profile';
const API_URL_KEY = 'bluff.apiUrl';
const RETRY_MS = 5000;

export type ConnectionStatus = 'idle' | 'connecting' | 'connected' | 'offline';

/** RoomView plus the local time it arrived, for running the turn timer without shared clocks. */
export type LiveRoom = RoomView & { receivedAt: number };

type GameApi = {
  ready: boolean;
  status: ConnectionStatus;
  profile: Profile | null;
  apiUrl: string;
  room: LiveRoom | null;
  notice: string | null;
  notify(message: string): void;
  setApiUrl(url: string): Promise<void>;
  enter(name: string): Promise<boolean>;
  quickPlay(bots: number, level: BotLevel): Promise<void>;
  createRoom(): Promise<void>;
  joinRoom(code: string): Promise<void>;
  addBot(level: BotLevel): Promise<void>;
  removeSeat(seatId: string): Promise<void>;
  startGame(): Promise<void>;
  playCards(cardIds: string[], rank: string): Promise<boolean>;
  callBluff(): Promise<void>;
  acceptFinish(): Promise<void>;
  leaveRoom(): Promise<void>;
};

const Context = createContext<GameApi | null>(null);

export function useGame(): GameApi {
  const api = useContext(Context);
  if (!api) throw new Error('useGame must be used inside GameProvider');
  return api;
}

const hubMessage = (error: unknown) => {
  const message = error instanceof Error ? error.message : '';
  const marker = 'HubException: ';
  const index = message.indexOf(marker);
  return index >= 0 ? message.slice(index + marker.length) : 'Bir şeyler ters gitti, tekrar dene.';
};

export function GameProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [status, setStatus] = useState<ConnectionStatus>('idle');
  const [profile, setProfile] = useState<Profile | null>(null);
  const [apiUrl, setApiUrlState] = useState(defaultApiUrl());
  const [room, setRoom] = useState<LiveRoom | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const hub = useRef<HubConnection | null>(null);
  const noticeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const notify = useCallback((message: string) => {
    setNotice(message);
    if (noticeTimer.current) clearTimeout(noticeTimer.current);
    noticeTimer.current = setTimeout(() => setNotice(null), 3500);
  }, []);

  const closeHub = useCallback(async () => {
    const current = hub.current;
    hub.current = null;
    await current?.stop().catch(() => undefined);
  }, []);

  const openHub = useCallback(
    async (url: string, player: Profile) => {
      await closeHub();
      setStatus('connecting');

      const connection = new HubConnectionBuilder()
        .withUrl(`${url}/hubs/game`, {
          accessTokenFactory: () => player.token,
          transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
          withCredentials: false,
        })
        .withAutomaticReconnect([0, 1000, 2000, 4000, 8000, 15000])
        .configureLogging(LogLevel.None)
        .build();

      connection.on('RoomUpdated', (view: RoomView) => {
        // Updates can overtake each other; only a newer version of a room counts.
        setRoom((previous) =>
          previous?.code === view.code && view.version < previous.version
            ? previous
            : { ...view, receivedAt: Date.now() },
        );
      });
      connection.on('RoomClosed', (reason: string) => {
        setRoom(null);
        if (reason) notify(reason);
      });
      connection.onreconnecting(() => setStatus('connecting'));
      connection.onreconnected(() => setStatus('connected'));
      connection.onclose(() => {
        if (hub.current === connection) setStatus('offline');
      });

      hub.current = connection;
      try {
        await connection.start();
        setStatus('connected');
      } catch (error) {
        setStatus('offline');
        if (error instanceof HttpError && error.statusCode === 401) throw new UnauthorizedError();
        throw error;
      }
    },
    [closeHub, notify],
  );

  const connectAs = useCallback(
    async (url: string, name: string, stored: Profile | null): Promise<void> => {
      let player = stored;
      if (player) {
        try {
          await renamePlayer(url, player.token, name);
          player = { ...player, name };
        } catch (error) {
          // The token belongs to another server or a reset database: start a new guest.
          if (!(error instanceof UnauthorizedError)) throw error;
          player = null;
        }
      }
      player ??= await createPlayer(url, name);

      await AsyncStorage.setItem(PROFILE_KEY, JSON.stringify(player));
      setProfile(player);

      if (hub.current?.state !== HubConnectionState.Connected) await openHub(url, player);
    },
    [openHub],
  );

  const enter = useCallback(
    async (name: string) => {
      try {
        await connectAs(apiUrl, name, profile);
        return true;
      } catch (error) {
        setStatus('offline');
        notify(error instanceof Error && error.message ? error.message : 'Sunucuya bağlanılamadı.');
        return false;
      }
    },
    [apiUrl, connectAs, notify, profile],
  );

  const setApiUrl = useCallback(
    async (url: string) => {
      const clean = url.trim().replace(/\/+$/, '');
      await closeHub();
      setRoom(null);
      setApiUrlState(clean);
      setStatus('idle');
      await AsyncStorage.setItem(API_URL_KEY, clean);
    },
    [closeHub],
  );

  // Resume after an app restart: a stored guest reconnects and gets their room back.
  useEffect(() => {
    (async () => {
      const [storedProfile, storedUrl] = await AsyncStorage.multiGet([PROFILE_KEY, API_URL_KEY]);
      const url = storedUrl[1] ?? defaultApiUrl();
      const player = storedProfile[1] ? (JSON.parse(storedProfile[1]) as Profile) : null;
      setApiUrlState(url);
      setProfile(player);
      setReady(true);
      if (player) await connectAs(url, player.name, player).catch(() => setStatus('offline'));
    })();
  }, [connectAs]);

  // Keep trying while offline, and right away when the app returns to the foreground.
  useEffect(() => {
    if (!profile || status !== 'offline') return;

    const retry = () => {
      connectAs(apiUrl, profile.name, profile).catch(() => setStatus('offline'));
    };
    const timer = setInterval(retry, RETRY_MS);
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') retry();
    });
    return () => {
      clearInterval(timer);
      subscription.remove();
    };
  }, [apiUrl, connectAs, profile, status]);

  const call = useCallback(
    async (method: string, ...args: unknown[]) => {
      if (hub.current?.state !== HubConnectionState.Connected) {
        notify('Bağlantı yok, tekrar deneniyor.');
        return false;
      }
      try {
        await hub.current.invoke(method, ...args);
        return true;
      } catch (error) {
        notify(hubMessage(error));
        return false;
      }
    },
    [notify],
  );

  const api = useMemo<GameApi>(
    () => ({
      ready,
      status,
      profile,
      apiUrl,
      room,
      notice,
      notify,
      setApiUrl,
      enter,
      quickPlay: async (bots, level) => void (await call('QuickPlay', bots, level)),
      createRoom: async () => void (await call('CreateRoom', 30)),
      joinRoom: async (code) => void (await call('JoinRoom', code)),
      addBot: async (level) => void (await call('AddBot', level)),
      removeSeat: async (seatId) => void (await call('RemoveSeat', seatId)),
      startGame: async () => void (await call('StartGame')),
      playCards: (cardIds, rank) => call('PlayCards', cardIds, rank),
      callBluff: async () => void (await call('CallBluff')),
      acceptFinish: async () => void (await call('AcceptFinish')),
      leaveRoom: async () => {
        await call('LeaveRoom');
        setRoom(null);
      },
    }),
    [apiUrl, call, enter, notice, notify, profile, ready, room, setApiUrl, status],
  );

  return <Context.Provider value={api}>{children}</Context.Provider>;
}
