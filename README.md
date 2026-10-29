# Practicum
ASP.NET Core API для событий и бронирований с хранением данных в PostgreSQL через EF Core.

Сущности создаются через `Event.Create(...)` и `Booking.Create(...)`: фабрики назначают идентификаторы, вместимость и начальное состояние. Сеттеры закрыты, навигация `Event.Bookings` доступна только для чтения. `Event.Update(...)` обновляет поля целиком и сохраняет число занятых мест; неудачная проверка вместимости не меняет состояние. Удаление выполняется через `Event.Delete()`, переходы статусов — через `Booking.Confirm()` и `Booking.Reject()`. Повторный переход не меняет время обработки, подтверждение отклонённой брони запрещено.

## Структура решения

| Проект | Назначение |
| --- | --- |
| `Domain` | Сущности `Event`, `Booking`, перечисление `BookingStatus` и доменные исключения. Не зависит от других проектов, ASP.NET Core и EF Core. |
| `Application` | Сценарии создания и изменения событий, бронирования, DTO, интерфейсы сервисов и порты репозиториев. Регистрация сервисов через `AddApplication()`. |
| `Infrastructure` | Реализации репозиториев, `AppDbContext`, конфигурации EF Core, миграции PostgreSQL и фоновый адаптер обработки бронирований. Регистрация через `AddInfrastructure(...)`. |
| `Presentation` | HTTP-контроллеры, глобальный обработчик исключений, настройки JSON и HTTP, composition root в `Program.cs`. Контроллеры передают DTO сервисам Application и формируют HTTP-ответы. |
| `Tests` | Проверки сервисов на EF Core InMemory, валидации DTO, сериализации и регистрации DI. |
| `IntegrationTests` | Проверки репозиториев и миграций на PostgreSQL 16 через Testcontainers. |

Ссылки между основными проектами направлены так:

```text
Presentation   -> Application, Infrastructure
Infrastructure -> Application, Domain
Application    -> Domain
Domain         -> нет ссылок на проекты
```

`Application` не ссылается на `Infrastructure`: доступ к данным описывают интерфейсы в `Application/Interfaces`, которые реализуют репозитории Infrastructure. Оба репозитория и `AppDbContext` имеют scoped-время жизни, поэтому изменения брони и мест сохраняются вместе. В `Program.cs` вызываются `AddApplication()`, `AddInfrastructure(...)` и `AddPresentation()`.

Операции записи Application выполняет через порт `IUnitOfWork.ExecuteAsync`. Реализация `UnitOfWork` в Infrastructure сохраняет изменения один раз, пропускает сохранение при отсутствии изменений и очищает несохранённое состояние при исключении. События и брони имеют теневое свойство `Version` в Infrastructure, которое хранится в PostgreSQL в колонке `version` и проверяется EF Core в условии `UPDATE`. `AppDbContext` назначает новую UUID-версию при каждой записи. Это защищает от потери обновлений между разными экземплярами API без глобального семафора. Порты репозиториев предоставляют `GetForUpdateAsync` для получения актуальной сущности перед изменением, не раскрывая EF Core, отслеживание или отключение сущностей.

Повтор ограничен 20 попытками с небольшой случайной задержкой, учитывающей отмену запроса. При исчерпании попыток ConcurrencyConflictException отображается в 409 Conflict. Делегат IUnitOfWork.ExecuteAsync может вызываться повторно: внешние побочные эффекты (например, отправку сообщений) в нём выполнять нельзя. Миграция AddConcurrencyVersions добавляет технические колонки к существующим данным без изменения доменных типов; она применяется при запуске API. Для исходных строк версия становится ненулевой при первой записи.

## Требования и запуск

- .NET SDK 10.x.
- Работающий PostgreSQL (в комплектном Docker Compose используется PostgreSQL 16).
- Docker с Docker Compose, если PostgreSQL запускается в контейнере.

Все команды выполняются из корня репозитория.

1. Клонируйте или скачайте репозиторий.
2. Запустите PostgreSQL:

   ```sh
   docker compose -f docker-compose_.yml up -d --wait
   ```

   База `eventapi` доступна на `localhost:5432`; пользователь и пароль — `postgres`. Данные контейнера сохраняются в именованном томе.
3. Настройте строку подключения в разделе `ConnectionStrings` файла [appsettings.json](Presentation/appsettings.json):

   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=localhost;Port=5432;Database=eventapi;Username=postgres;Password=postgres"
   }
   ```

   При использовании другого сервера измените `Host`, `Port`, `Database`, `Username` и `Password`. Строку можно переопределить переменной окружения `ConnectionStrings__DefaultConnection`.
4. Соберите и запустите API:

   ```sh
   dotnet build
   dotnet run --launch-profile https --project Presentation
   ```

5. Откройте [Swagger UI](https://localhost:7008/swagger/).

### Проверка через Swagger

Swagger доступен только в окружении `Development`, которое задаётся профилем запуска `https`. Используйте адрес HTTPS выше, чтобы запросы OpenAPI и API не зависели от перенаправления с HTTP.

1. В `POST /events` нажмите **Try it out**, задайте `title`, `description`, `startAt`, `endAt` (окончание позже начала), `totalSeats: 3` и нажмите **Execute**. Ожидается `201 Created`; сохраните `id` события из ответа.
2. Выполните `POST /events/{id}/book` с этим ID. Ожидается `202 Accepted`, статус `Pending` и ID брони.
3. Выполните `GET /bookings/{id}` с ID брони. Ожидается `200 OK`; через несколько секунд фоновая обработка изменит статус на `Confirmed` и заполнит `processedAt`.
4. Выполните `GET /events/{id}` с ID события. Ожидается `200 OK`, `totalSeats: 3`, `availableSeats: 2`.

### Миграции базы данных

После `builder.Build()` приложение вызывает `app.Services.ApplyInfrastructureMigrations()`. Этот extension-метод из Infrastructure создаёт scope и вызывает `Database.Migrate()`, применяя все ещё не выполненные миграции. Начальная миграция создаёт таблицы `events` и `bookings`, внешний ключ `bookings.event_id → events.id` и индекс по `event_id`. PostgreSQL и база `eventapi` должны существовать до запуска приложения; пользователь подключения должен иметь право создавать таблицы и индексы.

Последующие изменения модели оформляйте новыми миграциями. Для создания миграции выполните из корня репозитория, заменив `<MigrationName>` её названием:

```sh
dotnet ef migrations add <MigrationName> --project Infrastructure --startup-project Presentation
```

`--project Infrastructure` задаёт проект с `AppDbContext` и папкой `Migrations`, а `--startup-project Presentation` — запускаемый проект с конфигурацией и DI. Требуется инструмент `dotnet-ef` версии 10.x; если он не установлен, выполните `dotnet tool install --global dotnet-ef --version "10.*"`.

Проверить список миграций и применить их вручную можно командами:

```sh
dotnet ef migrations list --project Infrastructure --startup-project Presentation
dotnet ef database update --project Infrastructure --startup-project Presentation
```

База, ранее созданная через `EnsureCreated`, автоматически не преобразуется миграцией в новую схему; её данные и структуру нужно перенести отдельно.

Удаление события мягкое: оно скрывается из API и недоступно для новых броней, но история бронирований сохраняется. Ожидающие брони такого события переходят в `Rejected`, ранее подтверждённые сохраняют свой статус.

Даты без часового пояса трактуются как UTC; даты с часовым поясом нормализуются в UTC. Название ограничено 200 символами, описание — 2000; превышение длины и слишком большое смещение пагинации возвращают HTTP 400.

## Запуск тестов

`Tests` использует `Microsoft.EntityFrameworkCore.InMemory`, поэтому его можно запускать без PostgreSQL и Docker. `IntegrationTests` поднимает PostgreSQL 16 через Testcontainers; для этих тестов нужен работающий Docker.

В тестах сервисов `ServiceCollection` регистрирует `AppDbContext` с `UseInMemoryDatabase`, scoped-репозитории `IEventRepository` и `IBookingRepository`, единицу работы `IUnitOfWork`, а также сервисы `IEventService` и `IBookingService`. Имя InMemory-базы уникально для каждого теста. Параллельные запросы в тестах конкурентности получают отдельные scope и контексты.

Только тесты InMemory:

```sh
dotnet test Tests
```

Только PostgreSQL-интеграционные тесты:

```sh
dotnet test IntegrationTests
```

Для запуска обоих наборов из корня решения выполните:
```
dotnet test
```

Для запуска тестов с подробным выводом:
```
dotnet test --verbosity normal
```

### Проекты тестов

[`Tests`](Tests/Tests.csproj) проверяет поведение сервисов на InMemory-провайдере EF Core (без PostgreSQL и Docker):

- `EventServiceIntegrationTests` — покрывает операции `EventService`: создание, получение, обновление, удаление, фильтрацию по названию/датам, пагинацию, а также сценарии ошибок (`NotFoundException`, `ValidationException`). Это один `partial`-класс, разбитый по файлам по проверяемому поведению:
  - [`EventServiceIntegrationTests.cs`](Tests/EventServiceIntegrationTests.cs) — настройка DI и InMemory-базы, общий helper `CreateEventService()` и освобождение scope;
  - [`EventServiceCrudTests.cs`](Tests/EventServiceCrudTests.cs) — `Add` / `Get` / `Update` / `Delete` и их сценарии `NotFoundException`;
  - [`EventServiceFilteringTests.cs`](Tests/EventServiceFilteringTests.cs) — фильтрация `GetAll` по названию и диапазону дат;
  - [`EventServicePaginationTests.cs`](Tests/EventServicePaginationTests.cs) — пагинация `GetAll` и валидация `page` / `pageSize`;
  - [`EventServiceDateHandlingTests.cs`](Tests/EventServiceDateHandlingTests.cs) — нормализация дат и поведение низкоуровневых методов с сущностями. Методы `CreateAsync(EventDTO)` и `UpdateAsync(id, EventDTO)` проверяют DTO, включая порядок дат, независимо от HTTP-конвейера.
- [`BookingServiceIntegrationTests`](Tests/BookingServiceIntegrationTests.cs) — покрывает операции `BookingService`: создание брони для существующего события (включая уменьшение `AvailableSeats` события — после одной брони и после каждой из серии), получение по ID, переходы статусов (`Confirm`/`Reject`, возврат места через `ReleaseSeats`), а также сценарии ошибок (`NotFoundException` при несуществующем/удалённом событии, `NoAvailableSeatsException` при исчерпании мест). Один файл.

Тесты сервисов сгруппированы через `#region`:
- **Успешные сценарии**;
- **Неуспешные сценарии**;
- **Конкурентность** проверяется в `IntegrationTests/BookingConcurrencyTests.cs` на PostgreSQL: 30 параллельных запросов через два независимых DI-контейнера, атомарный откат конфликтующего сохранения, однократный возврат места при параллельных отказах и параллельная запись в разные события. EF InMemory не обеспечивает откат частично выполненного сохранения при конфликте и не используется для проверки атомарности конкурентных записей.

Используемые пакеты: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.EntityFrameworkCore.InMemory`, `Moq`, `coverlet.collector`.

[`IntegrationTests`](IntegrationTests/IntegrationTests.csproj) проверяет схему PostgreSQL и репозитории на настоящем PostgreSQL 16 в Testcontainers. Фикстура поднимает контейнер и создаёт отдельную базу для каждого теста. Набор проверяет применение миграций (таблицы и внешний ключ), а также все методы `EventRepository` и `BookingRepository`. Используемые пакеты включают `Testcontainers.PostgreSql`, `Microsoft.EntityFrameworkCore` и `Npgsql.EntityFrameworkCore.PostgreSQL`. Для запуска нужен работающий Docker.

## Формат ошибок API

Все ошибки возвращаются в единообразном формате JSON (RFC 7807 - Problem Details). Каждый экшен в `EventController` и `BookingController` помечен `[ProducesResponseType]` для всех кодов, которые он реально может вернуть (успех + доменные исключения) — без этого встроенный генератор `Microsoft.AspNetCore.OpenApi` не знает про ответы, брошенные из `GlobalExceptionHandlingMiddleware`, и в Swagger UI / `/openapi/v1.json` был бы виден только один код (обычно неверный «200 OK» вместо, например, реальных 201/202/204).

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Типы ошибок и статус-коды

| Статус | Название | Описание | Пример сценария |
|--------|----------|---------|-----------------|
| **404** | Resource not found | Ресурс не найден | `GET /events/{id}`, `GET /bookings/{id}` или `POST /events/{id}/book` — объект с указанным GUID не существует |
| **400** | Validation error | Ошибка валидации данных | `POST /events` с `endAt <= startAt` или `totalSeats <= 0`, `GET /events?page=0` |
| **409** | No available seats | На событии не осталось свободных мест | `POST /events/{id}/book`, когда `availableSeats = 0` |
| **500** | Internal server error | Ошибка сервера | Непредвиденная ошибка при обработке запроса |

### Примеры ошибок

**404 - Событие не найдено:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**404 - Бронь не найдена:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Booking\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/bookings/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**400 - Ошибка валидации:**
```json
{
  "title": "Validation error",
  "status": 400,
  "detail": "Дата окончания должна быть больше даты начала.",
  "instance": "/events"
}
```

**409 - Нет свободных мест:**
```json
{
  "title": "No available seats",
  "status": 409,
  "detail": "Нет свободных мест на это событие",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book"
}
```

---

## Endpoints

### 1. GET /events
Получить все события с опциональной фильтрацией и пагинацией.

**Параметры запроса:**
- `title` (string, опционально) - фильтрация по названию события (частичное совпадение, без учета регистра)
- `from` (DateTime, опционально) - фильтрация по дате начала (событие начинается не раньше указанной даты)
- `to` (DateTime, опционально) - фильтрация по дате окончания (событие заканчивается не позже указанной даты)
- `page` (integer, default: 1) - номер страницы (минимум: 1, если меньше → 400 Validation error)
- `pageSize` (integer, default: 10) - количество элементов на странице (диапазон: 1-100, за пределами → 400 Validation error)

**Валидация параметров пагинации:**
- `page` должен быть >= 1. При `page < 1` возвращается 400 ошибка
- `pageSize` должен быть в диапазоне 1-100. При нарушении возвращается 400 ошибка

**Примеры ошибок пагинации:**

```bash
# Ошибка: page = 0
curl -X 'GET' \
  'https://localhost:7008/events?page=0' \
  -H 'accept: text/plain'

# Ответ 400:
# {
#   "title": "Validation error",
#   "status": 400,
#   "detail": "Номер страницы должен быть больше или равен 1",
#   "instance": "/events?page=0"
# }

# Ошибка: pageSize = 150 (больше максимума)
curl -X 'GET' \
  'https://localhost:7008/events?pageSize=150' \
  -H 'accept: text/plain'

# Ответ 400:
# {
#   "title": "Validation error",
#   "status": 400,
#   "detail": "Размер страницы не может превышать 100",
#   "instance": "/events?pageSize=150"
# }
```

**Примеры корректного использования:**
```
# Получить все события на первой странице
curl -X 'GET' \
  'https://localhost:7008/events' \
  -H 'accept: text/plain'

# Получить события по названию с фильтрацией по датам
curl -X 'GET' \
  'https://localhost:7008/events?title=Conference&from=2024-01-01&to=2024-12-31&page=1&pageSize=10' \
  -H 'accept: text/plain'

# Получить события по названию
curl -X 'GET' \
  'https://localhost:7008/events?title=Workshop' \
  -H 'accept: text/plain'

# Получить события в диапазоне дат
curl -X 'GET' \
  'https://localhost:7008/events?from=2024-06-01&to=2024-12-31' \
  -H 'accept: text/plain'
```

**Ответ:**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "title": "Conference 2024",
      "description": "Annual tech conference",
      "startAt": "2024-06-15T09:00:00",
      "endAt": "2024-06-15T17:00:00",
      "totalSeats": 100,
      "availableSeats": 100
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1
}
```

### 2. GET /events/{id}
```
curl -X 'GET' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: text/plain'
```

### 3. POST /events
`totalSeats` обязателен и должен быть больше 0; `availableSeats` при создании всегда равен `totalSeats`.
```
curl -X 'POST' \
  'https://localhost:7008/events' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:48:22.985Z",
  "endAt": "2026-10-04T07:48:22.985Z",
  "totalSeats": 100
}'
```

### 4. PUT /events/{id}
`totalSeats` обязателен. `availableSeats` пересчитывается автоматически: число уже занятых мест сохраняется — клиент его не передаёт. Если новое `totalSeats` меньше числа уже занятых мест — `400 Validation error` (иначе фоновый сервис позже подтвердил бы лишние `Pending`-брони поверх новой вместимости).
```
curl -X 'PUT' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:54:29.355Z",
  "endAt": "2026-10-04T07:54:29.355Z",
  "totalSeats": 100
}'
```

### 5. POST /events/{id}/book
Создаёт бронь для указанного события и атомарно резервирует одно место. Если событие с таким ID не существует — `404 Resource not found`. Если свободных мест не осталось — `409 No available seats`. Все три кода (`202`/`404`/`409`) явно описаны через `[ProducesResponseType]` и видны в Swagger UI / `/openapi/v1.json`.

```
curl -X 'POST' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book' \
  -H 'accept: */*'
```

**Ответ:** `202 Accepted` с телом созданной брони:
```json
{
  "id": "e8f9d70a-1b2c-4d3e-8f4a-5b6c7d8e9f01",
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Pending",
  "createdAt": "2026-08-26T07:48:22.985Z",
  "processedAt": null
}
```

### 6. DELETE /events/{id}
```
curl -X 'DELETE' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: */*'
```

---

## Вместимость события (Seats)

Модель [`Event`](Domain/Models/Event.cs) хранит `totalSeats` (общее число мест, обязательно и должно быть больше 0) и `availableSeats` (свободные места; при создании равно `totalSeats`).

- События создаются через фабричный метод `Event.Create(...)`, который валидирует `totalSeats` и бросает `ValidationException` при `totalSeats <= 0`.
- `POST /events/{id}/book` вызывает `BookingService.CreateBookingAsync`: сервис получает событие для изменения и добавляет бронь через `IEventRepository` и `IBookingRepository`. Оба репозитория используют один scoped `AppDbContext`, поэтому резерв места и создание брони фиксируются одним `SaveChangesAsync()`. При отсутствии мест возвращается `409 No available seats`.
- Резервирование и освобождение мест инкапсулированы в сценариях создания и отклонения брони. Публичных методов сервисов для произвольного изменения числа мест нет; доменные операции проверяют положительное количество и ограничивают результат вместимостью.
- `PUT /events/{id}` пересчитывает `availableSeats` так, чтобы число уже занятых мест сохранилось, а не переносится как есть, и запрещает уменьшать `totalSeats` ниже числа уже занятых мест (`400 Validation error`) — иначе `availableSeats` пришлось бы либо уводить в минус, либо молча обнулять, оставляя лишние `Pending`-брони, которые фоновый сервис затем подтвердил бы поверх новой вместимости.
- `AppDbContext`, репозитории и сервисы зарегистрированы как scoped. Сервисы обращаются к данным только через `IEventRepository` и `IBookingRepository`; EF Core и `DbSet` используются в реализациях репозиториев. `UnitOfWork` из Infrastructure повторяет всю операцию при конфликте версии, перечитывая данные. Бронь и резерв места сохраняются одним атомарным `SaveChangesAsync`; проигравшая транзакция не оставляет лишней брони. Записи в разные события выполняются параллельно.

## Бронирования (Bookings)

Бронирования реализованы через [`IBookingService`](Application/Interfaces/IBookingService.cs) и [`BookingService`](Application/Services/BookingService.cs), который работает с PostgreSQL через [`IBookingRepository`](Application/Interfaces/IBookingRepository.cs) и [`IEventRepository`](Application/Interfaces/IEventRepository.cs). Контроллер [`BookingController`](Presentation/Controllers/BookingController.cs) обслуживает маршрут `/bookings`.

Бронирования в статусе `Pending` автоматически обрабатываются фоновым сервисом [`BookingProcessingService`](Infrastructure/Hosting/BookingProcessingService.cs): он периодически опрашивает хранилище и переводит ожидающие брони в статус `Confirmed`, имитируя обработку внешней системой. Подробнее — в разделе [Автоматическая обработка бронирований](#автоматическая-обработка-бронирований).

### Модель Booking

Поля модели [`Booking`](Domain/Models/Booking.cs):

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор брони (назначается автоматически) |
| `eventId` | `Guid` | Идентификатор события, на которое создана бронь |
| `status` | `BookingStatus` | Статус брони (см. ниже) |
| `createdAt` | `DateTime` | Время создания брони (UTC) |
| `processedAt` | `DateTime?` | Время обработки брони (если применимо) |

### Статусы брони (`BookingStatus`)

| Значение | Описание |
|----------|----------|
| `Pending` | Ожидает обработки (статус по умолчанию при создании). Автоматически переводится в `Confirmed` фоновым сервисом |
| `Confirmed` | Бронь подтверждена (перевод из `Pending` выполняет `BookingProcessingService`) |
| `Rejected` | Бронь отклонена — событие исчезло к моменту обработки, либо обработка завершилась неожиданной ошибкой (место при этом возвращается событию) |

Переходы между статусами инкапсулированы в методах [`Booking.Confirm()`](Domain/Models/Booking.cs) и `Booking.Reject()` — оба выставляют `Status` и `ProcessedAt = DateTime.UtcNow`.

Публичный контракт `IBookingService` содержит только получение и создание брони, обработку ожидающей брони (`ProcessPendingAsync`) и отказ (`RejectAsync`). Универсального изменения статуса, удаления и списка бронирований в контракте нет. Событие и время создания существующей брони изменить нельзя. Отказ освобождает место в той же единице работы, повторный отказ не освобождает его снова. Время обработки устанавливает сама сущность.

`IEventService` предоставляет список, получение, создание через `EventDTO`, обновление через `id` и `EventDTO` и мягкое удаление события. Тесты используют те же сценарии. Репозитории не предоставляют `SaveChangesAsync` и `Detach`: сохранением и очисткой после ошибок владеет `IUnitOfWork`, включая интеграционные тесты PostgreSQL.

Контроллеры возвращают DTO из `Application.DTOs`: `EventResponse` и `BookingResponse`, а список событий — `PaginatedResult<EventResponse>`. Маппинг явно перечисляет поля ответа. `EventResponse` не содержит `bookings` и `isDeleted`, `BookingResponse` не содержит обратной ссылки `event`: клиент не получает вводящий в заблуждение пустой список незагруженных навигаций. Доменные сущности не сериализуются в HTTP-ответах, специальный JSON-резолвер не нужен. Для контроллеров и генератора OpenAPI используются общие настройки строковой сериализации `BookingStatus`.

### GET /bookings/{id}
Получить бронь по идентификатору. Если бронь не найдена — `404 Resource not found`.

```
curl -X 'GET' \
  'https://localhost:7008/bookings/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: text/plain'
```

**Ответ:**
```json
{
  "id": "e8f9d70a-1b2c-4d3e-8f4a-5b6c7d8e9f01",
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Pending",
  "createdAt": "2026-08-26T07:48:22.985Z",
  "processedAt": null
}
```

### Автоматическая обработка бронирований

Фоновый адаптер [`BookingProcessingService`](Infrastructure/Hosting/BookingProcessingService.cs) наследует `BackgroundService` и регистрируется через `AddHostedService<BookingProcessingService>()` внутри [`AddInfrastructure(...)`](Infrastructure/DependencyInjection.cs). Он управляет опросом, DI-областями и восстановлением после сбоев; бизнес-переходы статусов выполняет `BookingService` из Application.

**Принцип работы:**

1. Сервис запускается вместе с приложением и каждые `PollingInterval` (**5 секунд**) создаёт DI scope и опрашивает `IBookingRepository`.
2. Выбираются все брони в статусе `Pending`.
3. Все найденные брони обрабатываются **параллельно** (`Task.WhenAll`) — при старте обработки каждая бронь сразу пишет в лог `Information`-сообщение (по нему видно, что несколько броней стартуют одновременно, а не по очереди), затем ждёт искусственную задержку `ProcessingDelay` (**2 секунды**), имитирующую обращение к внешней системе. Задержки идут одновременно, а не одна за другой.
4. `IBookingService.ProcessPendingAsync` перечитывает статус брони и событие; версии строк защищают сохранение при параллельной обработке:
   - если событие, на которое сделана бронь, к этому моменту удалено — бронь переводится в `Rejected` (`Booking.Reject()`), сохраняется, и пишется лог уровня `Warning`;
   - иначе бронь переводится в `Confirmed` (`Booking.Confirm()`) и сохраняется.
5. Если при обработке брони возникает непредвиденное исключение, создаётся новый scope для восстановления. `IBookingService.RejectAsync` сохраняет отказ и возврат места одним `SaveChangesAsync()` (одной транзакцией PostgreSQL). Повторный отказ не возвращает место второй раз. Ошибка записывается в лог уровня `Error`.
6. При остановке приложения (`CancellationToken`) сервис корректно завершает работу; `OperationCanceledException` пробрасывается из обработки конкретной брони и не считается ошибкой.

**Особенности реализации:**

- Для опроса и каждой параллельно обрабатываемой брони создаются отдельные scope через `IServiceScopeFactory`. Репозитории и `AppDbContext` не разделяются между параллельными задачами; при восстановлении после ошибки используется ещё один свежий scope.
- Ошибки при обработке не останавливают фоновый сервис — они логируются, после чего цикл продолжается.
- `OperationCanceledException` и `TaskCanceledException` при остановке приложения обрабатываются как штатное завершение.
- Глобальной блокировки записи нет: конфликтующие операции повторяются после отката и чтения актуальных версий. Повторный отказ уже отклонённой брони не возвращает место снова.

Это означает, что бронь, созданная через `POST /events/{id}/book` в статусе `Pending`, вскоре будет автоматически подтверждена (статус `Confirmed`) — если только событие не исчезло или не произошла ошибка, тогда бронь станет `Rejected`.

---

## Архитектура обработки ошибок

Проект использует единообразную систему обработки ошибок, основанную на собственных исключениях:

### Собственные исключения (`Domain.Exceptions`)

1. **`NotFoundException`** - выбрасывается, когда запрошенный ресурс не существует
   - Пример: `GET /events/{id}` когда события с указанным GUID нет
   - HTTP статус: 404

2. **`ValidationException`** - выбрасывается при ошибках валидации данных
   - Пример: попытка создать событие с `EndAt < StartAt` или `totalSeats <= 0`
   - HTTP статус: 400

3. **`NoAvailableSeatsException`** - выбрасывается, когда на событии не осталось свободных мест
   - Пример: `POST /events/{id}/book`, когда `availableSeats = 0`
   - HTTP статус: 409

Эти ошибки также переиспользуются в сервисе бронирований [`BookingService`](Application/Services/BookingService.cs):
- методы `GetAsync`, `ProcessPendingAsync` и `RejectAsync` выбрасывают `NotFoundException("Booking", id)` при обращении к несуществующему бронированию;
- `CreateBookingAsync` проверяет событие через `IEventRepository` и наличие мест, выбрасывая `NotFoundException` или `NoAvailableSeatsException` соответственно;
- сервис регистрируется в DI через интерфейс [`IBookingService`](Application/Interfaces/IBookingService.cs).

### Обработка исключений

**Middleware:** `GlobalExceptionHandlingMiddleware` перехватывает все необработанные исключения и преобразует их в единообразный JSON ответ (RFC 7807 - Problem Details).

**Преимущества:**
- Единый формат ошибок для всех endpoints
- Централизованная обработка ошибок
- Логирование всех ошибок
- Гибкая расширяемость (легко добавить новые типы исключений)
