# FarmIQ Telegram + ngrok Setup Guide

Bu hujjat `FarmIQ.API` ni local kompyuterda ishga tushirib, `ngrok` orqali internetga ochish va Telegram botni shu webhook'ka ulab test qilish uchun yozilgan.

Maqsad:

1. `FarmIQ.API` localda ishlaydi
2. `ngrok` local API'ni public HTTPS URL bilan ochadi
3. Telegram bot webhook'i shu URL'ga ulanadi
4. Siz Telegram ichidan xabar yuborib FarmIQ advisory flow'ni test qilasiz

## 1. Nimalar kerak

Kompyuteringizda quyidagilar bo'lishi kerak:

- `.NET 8 SDK`
- `PostgreSQL`
- `ngrok`
- Telegram account
- Telegram'da yaratilgan bot

## 2. FarmIQ ichida Telegram bilan bog'liq joylar

Ushbu loyihada Telegram webhook route'i:

```text
POST /api/webhooks/telegram
```

Controller:

- [WebhooksController.cs](/D:/sisharpchi/FarmIQ/src/FarmIQ.API/Controllers/WebhooksController.cs)

Telegram config joyi:

- [appsettings.json](/D:/sisharpchi/FarmIQ/src/FarmIQ.API/appsettings.json)

Muhim sectionlar:

```json
"Webhooks": {
  "TelegramSecretToken": "telegram-secret"
},
"ChannelApis": {
  "TelegramBaseUrl": "https://api.telegram.org",
  "TelegramBotToken": "YOUR_BOT_TOKEN"
}
```

## 3. Telegram bot yaratish

### 3.1 BotFather orqali yangi bot ochish

Telegram ichida [@BotFather](https://t.me/BotFather) ga kiring va quyidagilarni qiling:

1. `/newbot`
2. Bot nomini kiriting
3. Bot username kiriting, masalan `testfarmiq_bot`
4. Bot tokenni oling

Token ko'rinishi odatda shunaqa bo'ladi:

```text
123456789:AA...
```

## 4. FarmIQ config tayyorlash

### 4.1 Telegram bot tokenni qo'yish

[appsettings.json](/D:/sisharpchi/FarmIQ/src/FarmIQ.API/appsettings.json) ichida:

```json
"ChannelApis": {
  "TelegramBaseUrl": "https://api.telegram.org",
  "TelegramBotToken": "YOUR_BOT_TOKEN"
}
```

`YOUR_BOT_TOKEN` o'rniga BotFather bergan tokenni yozing.

### 4.2 Telegram secret token qo'yish

Xuddi shu faylda:

```json
"Webhooks": {
  "TelegramSecretToken": "telegram-secret"
}
```

Buni o'zingizga qulay random qiymatga almashtiring, masalan:

```json
"Webhooks": {
  "TelegramSecretToken": "farmiq-telegram-local-2026"
}
```

## 5. Database va API'ni ishga tushirish

Avval PostgreSQL ishlayotgan bo'lishi kerak.

### 5.1 API'ni ishga tushirish

PowerShell'da:

```powershell
dotnet run --project src/FarmIQ.API
```

API odatda quyidagi local URL'larda turadi:

- `https://localhost:7127`
- `http://localhost:5015`

Telegram webhook uchun local HTTPS sertifikat bilan ovora bo'lmaslik uchun `http://localhost:5015` ni `ngrok` orqali ochish eng qulay yo'l.

## 6. ngrok orqali local API'ni ochish

Yangi terminal oching va quyidagini ishga tushiring:

```powershell
ngrok http 5015
```

Yoki aniq local host bilan:

```powershell
ngrok http http://localhost:5015
```

`ngrok` sizga public URL beradi, masalan:

```text
https://abc123.ngrok-free.app
```

Bizga kerak bo'ladigan webhook URL:

```text
https://abc123.ngrok-free.app/api/webhooks/telegram
```

## 7. Telegram webhook'ni ulash

### 7.1 Webhook qo'yish

Brauzer yoki PowerShell orqali quyidagi so'rovni yuboring:

```powershell
$botToken = "YOUR_BOT_TOKEN"
$publicBaseUrl = "https://abc123.ngrok-free.app"
$secretToken = "farmiq-telegram-local-2026"

Invoke-RestMethod -Method Post `
  -Uri "https://api.telegram.org/bot$botToken/setWebhook" `
  -ContentType "application/json" `
  -Body (@{
    url = "$publicBaseUrl/api/webhooks/telegram"
    secret_token = $secretToken
    allowed_updates = @("message", "edited_message")
  } | ConvertTo-Json)
```
ishlaydi pw: Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot8796603206:AAG8o7YBpXipJ72bQ_3jV_w1XvH-OokqkzE/setWebhook" -ContentType "application/json" -Body (@{ url = "https://0abb-109-94-174-164.ngrok-free.app/api/webhooks/telegram"; secret_token = "telegram-secret"; allowed_updates = @("message","edited_message") } | ConvertTo-Json)

Muvaffaqiyatli javob odatda shunga o'xshaydi:

```json
{
  "ok": true,
  "result": true,
  "description": "Webhook was set"
}
```

### 7.2 Webhook holatini tekshirish

```powershell
Invoke-RestMethod -Method Get `
  -Uri "https://api.telegram.org/botYOUR_BOT_TOKEN/getWebhookInfo"
```
Invoke-RestMethod -Method Get -Uri "https://api.telegram.org/bot8796603206:AAG8o7YBpXipJ72bQ_3jV_w1XvH-OokqkzE/getWebhookInfo"

Natijada `url` sizning `ngrok` URL'ingizga qaragan bo'lishi kerak.

## 8. Endi Telegram'da test qilish

Botingizga kiring va quyidagilarni yuborib ko'ring.

### 8.1 Oddiy onboarding testi

```text
/start
```

Kutiladigan natija:

- bot welcome/onboarding message qaytaradi
- advisory job yaratmasligi kerak

### 8.2 Text symptom testi

```text
my tomato leaves have spots
```

Kutiladigan natija:

- advisory flow ishga tushadi
- bot crop issue bo'yicha javob qaytaradi

### 8.3 Aphid/pest testi

```text
aphids are spreading on my tomato leaves
```

Kutiladigan natija:

- generic greeting emas
- pest-focused advisory qaytadi

### 8.4 Photo testi

Telegram botga:

- crop rasmi yuboring
- iloji bo'lsa caption ham yozing

Masalan:

```text
tomato leaves have brown spots
```

### 8.5 Voice testi

Voice note yuboring.

Agar caption qo'sha olsangiz, qo'shing. Masalan:

```text
tomato leaves are turning yellow
```

### 8.6 Location testi

Telegram ichida location share qiling.

Bu keyingi advisory'larda weather summary uchun foydali bo'ladi.

## 9. Admin panel orqali tekshirish

Agar `FarmIQ.Admin` ham ishlayotgan bo'lsa:

```powershell
dotnet run --project src/FarmIQ.Admin
```

Keyin admin panelga kiring:

- `https://localhost:7118`

Quyidagi sahifalarda natijani ko'rasiz:

- `/dashboard`
- `/conversations`
- `/jobs`
- `/advisories`
- `/deliveries`
- `/system`

Nimalarni ko'rish mumkin:

- kelgan Telegram xabari
- created conversation
- advisory job holati
- advisory result
- duplicate event bor-yo'qligi

## 10. Qulay test ketma-ketligi

Eng amaliy local test tartibi:

1. PostgreSQL ni ishga tushiring
2. `dotnet run --project src/FarmIQ.API`
3. boshqa terminalda `ngrok http 5015`
4. `setWebhook` qiling
5. Telegram botga `/start` yuboring
6. keyin symptom text yuboring
7. keyin photo yuboring
8. admin panelda natijani tekshiring

## 11. Foydali Telegram API komandalar

### Webhook info

```powershell
Invoke-RestMethod -Method Get `
  -Uri "https://api.telegram.org/botYOUR_BOT_TOKEN/getWebhookInfo"
```

### Webhookni olib tashlash

```powershell
Invoke-RestMethod -Method Get `
  -Uri "https://api.telegram.org/botYOUR_BOT_TOKEN/deleteWebhook"
```

### Pending update'larni tozalab yangi webhook qo'yish

```powershell
$botToken = "YOUR_BOT_TOKEN"
$publicBaseUrl = "https://abc123.ngrok-free.app"
$secretToken = "farmiq-telegram-local-2026"

Invoke-RestMethod -Method Post `
  -Uri "https://api.telegram.org/bot$botToken/setWebhook" `
  -ContentType "application/json" `
  -Body (@{
    url = "$publicBaseUrl/api/webhooks/telegram"
    secret_token = $secretToken
    drop_pending_updates = $true
    allowed_updates = @("message", "edited_message")
  } | ConvertTo-Json)
```

## 12. Loglarda nimalarni ko'rish kerak

API console'da odatda quyidagilar ko'rinadi:

- Telegram webhook accepted
- inbound message parsed
- processing job queued
- worker jobni claim qilgani
- advisory sent bo'lgani

Agar hammasi to'g'ri ishlasa, Telegram bot sizga javob qaytaradi.

## 13. Eng ko'p uchraydigan muammolar

### Muammo: Telegram javob bermayapti

Tekshiring:

- `FarmIQ.API` ishlayaptimi
- `ngrok` URL hali aktivmi
- webhook to'g'ri URL'ga qo'yilganmi
- `ChannelApis:TelegramBotToken` to'g'rimi

### Muammo: Webhook `ngrok` eski URL'da qolib ketgan

`ngrok` qayta ishga tushsa, URL o'zgaradi. Har safar yangi URL olsangiz, `setWebhook` ni yana bir marta yuborishingiz kerak.

### Muammo: Telegram bot xabar oladi, lekin javob qaytarmaydi

Tekshiring:

- `ChannelApis:TelegramBotToken` bo'sh emasmi
- outbound send vaqtida API logda xato chiqmadimi
- advisory worker completed bo'ldimi

### Muammo: Advisory job yaratilmayapti

Tekshiring:

- `Conversations` sahifasida inbound bor-yo'qligini
- `Jobs` sahifasida pending/in-progress job borligini
- duplicate delivery bo'lib qolmaganini

### Muammo: `401` yoki webhook xato

Telegram webhook URL to'g'ri bo'lishi kerak:

```text
https://YOUR_NGROK_DOMAIN/api/webhooks/telegram
```

Bot token esa faqat `setWebhook` va `sendMessage` uchun ishlatiladi, webhook URL ichiga token qo'shilmaydi.

## 14. Tavsiya etilgan local qiymatlar

Development uchun quyidagiga o'xshash config ishlatish qulay:

```json
"Webhooks": {
  "TelegramSecretToken": "farmiq-telegram-local-2026"
},
"ChannelApis": {
  "TelegramBaseUrl": "https://api.telegram.org",
  "TelegramBotToken": "YOUR_BOT_TOKEN"
}
```

## 15. Qisqa xulosa

Telegram + ngrok test flow juda qisqa:

1. API run qiling
2. `ngrok http 5015`
3. `setWebhook`
4. botga yozing
5. admin panelda tekshiring

Shu bilan FarmIQ'ning Telegram webhook oqimini real messenjer orqali local kompyuterda sinab ko'rishingiz mumkin.
