# Blöf

Blöf kart oyununun mobil sürümü. 2-4 kişi, gerçek oyuncular ve botlarla oynanır.

- `mobile/`: React Native (Expo SDK 58, TypeScript)
- `backend/`: .NET 10, ASP.NET Core, SignalR, SQLite
  - `src/Bluff.Core`: oyun kuralları ve yapay zekâ
  - `src/Bluff.Api`: odalar, gerçek zamanlı bağlantı, skorlar
  - `tests/Bluff.Tests`: testler

## Çalıştırma

Gerekenler: Node 22 ve .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

Backend (`http://0.0.0.0:5080` adresini dinler):

```
cd backend
dotnet run --project src/Bluff.Api
```

Farklı port için `--urls http://0.0.0.0:PORT` ver; `appsettings.json` ayarı `ASPNETCORE_URLS` değişkeninden önceliklidir.

Mobil:

```
cd mobile
npm install
npx expo start
```

Çıkan QR kodu telefondaki Expo Go ile okut (Expo Go, SDK 58 sürümü olmalı). Telefon ve bilgisayar aynı
Wi-Fi'de olmalı. Windows Güvenlik Duvarı 5080 portunu engelliyorsa izin ver:

```
netsh advfirewall firewall add rule name="Blof API" dir=in action=allow protocol=TCP localport=5080
```

Uygulama sunucu adresini Expo sunucusunun çalıştığı bilgisayardan otomatik alır. Değiştirmek için ana
ekranın altındaki bağlantı satırına dokun ya da `EXPO_PUBLIC_API_URL` değişkenini ayarla.

Web sürümü: mobil kod aynı zamanda web sitesidir ve backend tarafından sunulur.

```
cd mobile
npm run build:web
cd ../backend
dotnet run --project src/Bluff.Api
```

Sonra tarayıcıda `http://localhost:5080` adresini aç. Aynı ağdaki biri `http://<bilgisayar-ip>:5080` ile
girebilir. Web ve mobil oyuncular aynı odada oynayabilir. Kod değiştiğinde `npm run build:web` yeniden
çalıştırılmalı; geliştirme sırasında `npx expo start --web` de kullanılabilir.

Android emülatöründe adres otomatik bulunmazsa: `adb reverse tcp:5080 tcp:5080`, adres `http://localhost:5080`.

Testler: `cd backend && dotnet test`. Tür kontrolü: `cd mobile && npx tsc --noEmit`.

## Kurallar

1. 52 kart oyunculara olabildiğince eşit dağıtılır; ilk oyuncu rastgele seçilir.
2. Sıradaki oyuncu 1-4 kartı kapalı atar ve değerini söyler. Söylediği değer yalan olabilir, adet ise
   her zaman atılan kart sayısıdır.
3. Turu açan değeri seçer; tur boyunca herkes aynı değeri söyler.
4. Sıradaki oyuncu kart atmak yerine "Blöf!" diyebilir. Son atılan kartlar açılır. Yalan varsa atan, yoksa
   itiraz eden masadaki bütün kartları alır.
5. İtirazı kazanan yeni turu açar.
6. Son kartını atan oyuncu, sıradaki oyuncu kabul ederse ya da itiraz edip haksız çıkarsa kazanır. Blöfü
   yakalanırsa kartları alır, oyun sürer.

Tanımda açık olmayan iki noktada verilen karar: yalnızca sıradaki oyuncu itiraz edebilir, pas geçme yoktur.

## Çevrim içi davranış

- Kurallar sunucuda işler. İstemci yalnızca kendi elini ve herkese açık bilgiyi alır.
- Botlar da yalnızca kendi ellerini, herkese açık hamleleri ve itirazlarda açılan kartları görür.
- Süresi dolan oyuncu için otomatik hamle yapılır (süre 30 sn; oda kurarken API 15/30/60 kabul eder).
- Bağlantısı kopan oyuncunun sırası 5 sn bekler, sonra otomatik oynanır. Geri dönünce oda kaldığı yerden sürer.
- Oyundan ayrılan oyuncunun yerine bot geçer, ayrılan oyuncuya mağlubiyet yazılır.
- Hiç bağlı oyuncu yokken oyun bekler; 10 dakika sonra oda silinir.
- Odalar bellekte tutulur, sunucu yeniden başlarsa süren oyunlar kaybolur. Skorlar `bluff.db` dosyasında kalır.

## Yayına alırken

- Mobil uygulama şu an HTTP ile bağlanır. Yayında sunucuyu HTTPS arkasına al ve `EXPO_PUBLIC_API_URL` ver.
- Web sitesi backend'le aynı adresten sunulduğu için CORS ayarı gerekmez. Siteyi başka bir adreste
  barındırırsan derlemeden önce `EXPO_PUBLIC_API_URL` ver ve `Cors:AllowedOrigins` ayarını doldur.
- Kurulabilir APK için: `npx eas-cli@latest build -p android`.
