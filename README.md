# FarmIQ

FarmIQ bu kichik va o'rta fermerlar uchun mo'ljallangan AI crop advisory platforma. Tizim WhatsApp, Telegram va Instagram kabi messenjerlardan kelgan xabarlarni qabul qiladi, ovoz va rasmni tahlil qiladi, ob-havo ma'lumotini qo'shadi va fermerga o'simlik holati bo'yicha tavsiya qaytaradi.

Bu repository hozirgi holatda ikkita asosiy ichki mahsulotni o'z ichiga oladi:

- `FarmIQ.API` - webhooklar, advisory pipeline, autentifikatsiya, admin API va background worker
- `FarmIQ.Admin` - ichki operatorlar va analystlar uchun Blazor admin panel

Loyiha Clean Architecture tamoyili asosida qurilgan va hozir production v1 darajasiga yaqinlashtirilgan: durable job processing, PostgreSQL, OpenIddict token auth, invite-only admin access, Docker Compose deploy va Nginx reverse proxy bilan.

## 1. Loyiha nimani hal qiladi

Ko'plab kichik fermerlarda quyidagi muammolar bor:

- agronomga tez chiqish imkoniyati yo'q
- internetdan qidirish har doim ham qulay emas
- kasallik yoki zararkunanda tez tarqalganda javob darhol kerak bo'ladi
- tavsiya lokal tilga va sodda formatga mos bo'lishi kerak

FarmIQ shu muammoni chat-first model bilan hal qiladi:

1. Fermer messenjer orqali xabar yuboradi
2. Rasm, caption yoki voice qabul qilinadi
3. Tizim rasm va matnni normalizatsiya qiladi
4. Voice bo'lsa transcription qilinadi
5. Crop analysis va weather summary ishlaydi
6. Advisory yaratiladi
7. Javob aynan shu kanalga qaytariladi

## 2. Asosiy imkoniyatlar

### Farmer taraf

- WhatsApp webhook support
- Telegram webhook support
- Instagram webhook support
- rasmli xabarlarni qabul qilish
- voice / audio xabarlarni qabul qilish
- caption yoki text bilan advisory yaratish
- weather summary qo'shish
- crop disease / pest mock AI tahlili

### Admin taraf

- dashboard
- conversations ro'yxati va detail timeline
- processing jobs monitoring
- stuck jobs ko'rish
- duplicate delivery issues ko'rish
- advisories ro'yxati va detail ko'rish
- system readiness/status ko'rish
- users management:
  - user yaratish
  - role berish
  - disable / enable qilish
  - password reset qilish

### Platforma taraf

- PostgreSQL-backed durable jobs
- lease-based worker claim/retry modeli
- duplicate webhook delivery protection
- OpenIddict password grant token flow
- admin session `sessionStorage` orqali ishlaydi
- public signup developmentda yoqilishi mumkin, productionda odatda o'chiriladi
- Docker Compose bilan single VPS deploy

## 3. Texnologiyalar

### Backend

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- OpenIddict
- ASP.NET Core Identity

### Admin UI

- Blazor Web App
- Interactive Server render mode
- JWT-based browser session model

### Infra

- Nginx
- Docker
- Docker Compose

## 4. Solution tuzilmasi

Repository ichidagi asosiy papkalar:

```text
src/
  FarmIQ.Core
  FarmIQ.Application
  FarmIQ.Infrastructure
  FarmIQ.API
  FarmIQ.Admin
  FarmIQ.Shared

tests/
  FarmIQ.Tests
```

### Qisqacha har bir layer vazifasi

- `FarmIQ.Core`
  Domain entitylar va bazaviy model

- `FarmIQ.Application`
  interface, contract, workflow va business orchestration

- `FarmIQ.Infrastructure`
  EF Core, Identity, OpenIddict, channel service, weather, media storage, background worker

- `FarmIQ.API`
  HTTP endpointlar, webhook controllerlar, auth/token endpointlar, admin API

- `FarmIQ.Admin`
  operator dashboard va ichki admin panel

- `FarmIQ.Shared`
  umumiy enum va primitive model

## 5. Tizim qanday ishlaydi

### 5.1 Inbound advisory flow

Fermerdan kelgan xabar quyidagi ketma-ketlikda ishlanadi:

1. Tashqi provider webhook chaqiradi
2. `FarmIQ.API` webhook controller requestni qabul qiladi
3. Provider-specific payload normalized command formatga o'tkaziladi
4. Raw payload va inbound message databasega yoziladi
5. Duplicate delivery bo'lsa qayta advisory yaratmaydi
6. Processing job databasega yoziladi
7. Background worker pending jobni claim qiladi
8. Media local storagega saqlanadi
9. Voice bo'lsa transcription ishlaydi
10. Text + voice birlashtiriladi
11. AI crop analysis ishlaydi
12. Weather summary olinadi
13. Advisory yaratiladi va saqlanadi
14. Outbound message shu kanalga qaytariladi

### 5.2 Worker modeli

Worker in-memory queue'ga bog'lanib qolmagan, durable model ishlatiladi:

- joblar PostgreSQL ichida saqlanadi
- worker pending yoki expired lease bo'lgan jobni claim qiladi
- har jobda retry count bor
- terminal failure bo'lsa dead-letter reason saqlanadi
- app restart bo'lsa ham joblar yo'qolmaydi

### 5.3 Admin auth modeli

Admin panel cookie auth bilan emas, browser-side JWT session modeli bilan ishlaydi:

- user `/connect/token` orqali access token oladi
- token `sessionStorage`ga yoziladi
- admin UI auth state shu session asosida tiklanadi
- session tugasa local state tozalanadi va user `/login`ga qaytadi

## 6. Muhim endpointlar

### Public / channel-facing

- `POST /api/webhooks/whatsapp`
- `GET /api/webhooks/telegram`
- `POST /api/webhooks/telegram`
- `GET /api/webhooks/instagram`
- `POST /api/webhooks/instagram`
- `POST /connect/token`

### Auth

- `POST /api/auth/signup`

Eslatma:

- developmentda signup yoqilishi mumkin
- productionda odatda `Auth:EnablePublicSignup=false`

### Admin API

- `GET /api/admin/analytics`
- `GET /api/admin/conversations`
- `GET /api/admin/conversations/{id}`
- `GET /api/admin/jobs`
- `GET /api/admin/jobs/stuck`
- `POST /api/admin/jobs/retry`
- `GET /api/admin/deliveries/issues`
- `GET /api/admin/advisories`
- `GET /api/admin/advisories/{id}`
- `GET /api/admin/status`
- `GET /api/admin/session`
- `GET /api/admin/users`
- `POST /api/admin/users`
- `POST /api/admin/users/{userId}/disable`
- `POST /api/admin/users/{userId}/enable`
- `POST /api/admin/users/{userId}/reset-password`
- `POST /api/admin/users/{userId}/roles`

### Health

- `GET /health`
- `GET /health/live`
- `GET /health/ready`

## 7. Admin UI sahifalari

Admin panel ichida quyidagi route'lar ishlaydi:

- `/login`
- `/signup`
- `/dashboard`
- `/conversations`
- `/jobs`
- `/deliveries`
- `/advisories`
- `/system`
- `/users`

### Role bo'yicha access

- `Admin`
  barcha sahifalar va user management

- `Ops`
  jobs, deliveries, dashboard, system, conversations, advisories

- `Analyst`
  dashboard, conversations, advisories, system

## 8. Local developmentda ishga tushirish

Quyidagi usul Windows PowerShell uchun yozilgan.

### 8.1 Talablar

Kompyuterda quyidagilar o'rnatilgan bo'lishi kerak:

- .NET SDK 8
- PostgreSQL
- Git ixtiyoriy
- Visual Studio 2022 yoki VS Code ixtiyoriy

### 8.2 Database tayyorlash

PostgreSQL ichida `farmiq` database yarating yoki `appsettings.json`dagi connection stringni o'zingizga moslang.

Default development config:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=farmiq;Username=postgres;Password=1"
}
```

Kerak bo'lsa `src/FarmIQ.API/appsettings.json` ichida o'zgartiring.

### 8.3 API'ni run qilish

```powershell
dotnet run --project src/FarmIQ.API
```

API default development URLlari:

- `https://localhost:7127`
- `http://localhost:5015`

Swagger developmentda:

- `https://localhost:7127/swagger`

### 8.4 Admin UI'ni run qilish

```powershell
dotnet run --project src/FarmIQ.Admin
```

Admin UI default development URLlari:

- `https://localhost:7118`
- `http://localhost:5059`

### 8.5 Login qilish

API startup vaqtida seed admin yaratadi. Default qiymatlar:

- email: `admin@farmiq.local`
- password: `FarmIQ!123`

Bu qiymatlar `src/FarmIQ.API/appsettings.json` ichidagi `SeedAdmin` section orqali boshqariladi.

Developmentda signup yoqilgan bo'lsa, `/signup` orqali ham account ochish mumkin.

## 9. Visual Studio orqali ishga tushirish

### API uchun

1. Solution oching
2. `FarmIQ.API`ni startup project qiling
3. `https` profile bilan run qiling

### Admin uchun

1. `FarmIQ.Admin`ni startup project qiling
2. `https` profile bilan run qiling

Ko'pincha ikkala projectni parallel run qilinadi:

- API alohida
- Admin alohida

## 10. Docker Compose bilan productionga o'xshash ishga tushirish

Repository ichida quyidagi fayllar qo'shilgan:

- `src/FarmIQ.API/Dockerfile`
- `src/FarmIQ.Admin/Dockerfile`
- `docker-compose.yml`
- `deploy/nginx/default.conf`
- `.env.example`

### 10.1 Environment fayl tayyorlash

`.env.example`dan nusxa oling:

```powershell
Copy-Item .env.example .env
```

Keyin `.env` ichidagi secret va tokenlarni real qiymatlar bilan to'ldiring.

### 10.2 Stackni ko'tarish

```powershell
docker compose up -d --build
```

### 10.3 Stack tarkibi

Compose ichida quyidagi containerlar ishlaydi:

- `farmiq-postgres`
- `farmiq-api`
- `farmiq-admin`
- `farmiq-nginx`

### 10.4 Browserdan ochish

Nginx `80` portda ochiladi:

- `http://YOUR_SERVER_IP/`

Shu route Admin UI'ga olib boradi.

Nginx quyidagicha proxy qiladi:

- `/` -> `FarmIQ.Admin`
- `/api/*` -> `FarmIQ.API`
- `/connect/*` -> `FarmIQ.API`
- `/health*` -> `FarmIQ.API`
- `/swagger/*` -> `FarmIQ.API`

### 10.5 Nima uchun Nginx kerak

Nginx quyidagi ishlarni qiladi:

- Admin va API'ni bitta domen / origin ostida beradi
- reverse proxy vazifasini bajaradi
- Blazor server websocket / interactive trafficni uzatadi
- future TLS terminatsiya uchun joy tayyorlaydi

## 11. Muhim konfiguratsiyalar

### API config

`src/FarmIQ.API/appsettings.json` ichida:

- `ConnectionStrings:DefaultConnection`
- `OpenWeatherMap:*`
- `Storage:*`
- `SeedAdmin:*`
- `Webhooks:*`
- `ChannelApis:*`
- `Processing:*`
- `Auth:*`

### Admin config

`src/FarmIQ.Admin/appsettings.json` ichida:

- `Api:BaseUrl`
- `Features:EnablePublicSignup`
- `Polling:*`

### Eng muhim env variablelar

- `POSTGRES_PASSWORD`
- `SEED_ADMIN_EMAIL`
- `SEED_ADMIN_PASSWORD`
- `WHATSAPP_VERIFY_TOKEN`
- `TELEGRAM_SECRET_TOKEN`
- `INSTAGRAM_VERIFY_TOKEN`
- `OPENWEATHER_API_KEY`
- `AUTH_ENABLE_PUBLIC_SIGNUP`

## 12. Background worker haqida

Workerning asosiy vazifasi advisory pipeline'ni async ishlatish.

Ishlash printsipi:

- webhook request tez javob qaytaradi
- og'ir ishlar background worker'ga ketadi
- worker database ichidan job claim qiladi
- lease tugasa jobni boshqa worker qayta olishi mumkin

Bu model production uchun foydali, chunki:

- app restart bo'lsa job yo'qolmaydi
- duplicate concurrent processing kamayadi
- retry/backoff ishlaydi
- failed joblar admin paneldan ko'rinadi

## 13. Identity va access control

Loyihada ASP.NET Core Identity + OpenIddict ishlatiladi.

### Asosiy role'lar

- `Admin`
- `Ops`
- `Analyst`

### Invite-only access

Productionda odatda:

- public signup o'chiriladi
- yangi userni faqat mavjud admin yaratadi
- userlar `/users` sahifasi orqali boshqariladi

## 14. Test va verification

Testlarni ishga tushirish:

```powershell
dotnet test tests/FarmIQ.Tests/FarmIQ.Tests.csproj
```

Build qilish:

```powershell
dotnet build FarmIQ.sln
```

Faqat API build:

```powershell
dotnet build src/FarmIQ.API/FarmIQ.API.csproj
```

Faqat Admin build:

```powershell
dotnet build src/FarmIQ.Admin/FarmIQ.Admin.csproj
```

## 15. Health checklar nimani bildiradi

### `/health/live`

App process tirikmi yoki yo'qmi shuni bildiradi.

### `/health/ready`

Quyidagilar tayyormi shuni bildiradi:

- database connection
- worker heartbeat

Bu endpoint production monitoring uchun muhim.

## 16. Odatdagi muammolar va yechimlar

### Muammo: Admin login bo'lmayapti

Tekshiring:

- API ishlayaptimi
- `FarmIQ.Admin` ichidagi `Api:BaseUrl` to'g'rimi
- seed admin email/password to'g'rimi
- user disable bo'lib qolmaganmi

### Muammo: `/conversations` yoki boshqa page ochilmayapti

Tekshiring:

- Admin project yangi build qilinganmi
- browser cache tozalanganmi
- user session expired bo'lmaganmi

### Muammo: Worker joblarni olmayapti

Tekshiring:

- database ulanishi borligini
- `Processing` config to'g'riligini
- `/health/ready` statusini
- admin `Jobs` sahifasida terminal failure yoki stuck holatlar bor-yo'qligini

### Muammo: Token endpoint ishlamayapti

Tekshiring:

- `FarmIQ.API` run bo'layotganini
- OpenIddict migrationlar apply bo'lganini
- database to'g'ri ekanini

### Muammo: Docker Compose turmayapti

Tekshiring:

- `.env` fayl yaratilganmi
- `POSTGRES_PASSWORD` va webhook tokenlar berilganmi
- `docker compose logs` natijasini

## 17. CI haqida

Repository ichida GitHub Actions workflow bor:

- `.github/workflows/ci.yml`

U quyidagilarni bajaradi:

- restore
- build
- test
- API Docker image build
- Admin Docker image build

## 18. Hozirgi cheklovlar

Hozirgi versiya production-minded MVP / v1 bo'lib, quyidagi narsalar keyingi bosqichga qolishi mumkin:

- real AI model integrationni chuqurlashtirish
- email invite flow
- farmer-facing public web portal
- SignalR live updates
- cloud media storage
- multi-tenant hard isolation

## 19. Tez start uchun eng qisqa yo'l

Agar faqat local sinab ko'rmoqchi bo'lsangiz:

1. PostgreSQL tayyorlang
2. `dotnet run --project src/FarmIQ.API`
3. `dotnet run --project src/FarmIQ.Admin`
4. `https://localhost:7118`ni oching
5. seed admin bilan login qiling

Agar productionga o'xshash ishga tushirmoqchi bo'lsangiz:

1. `.env.example` -> `.env`
2. secretlarni to'ldiring
3. `docker compose up -d --build`
4. server IP orqali login qiling

## 20. Xulosa

FarmIQ hozirgi holatda ichki ops/admin platforma sifatida ishlashga tayyorlangan:

- multi-channel webhook ingestion bor
- advisory pipeline bor
- durable job processing bor
- admin UI bor
- user management bor
- Docker Compose deploy tayyor

Ya'ni bu faqat demo emas, balki real startup backendini productionga olib chiqish uchun kerak bo'ladigan asosiy karkas va operatsion qatlamlarni o'z ichiga oladi.
