# Practicum
PracticumApi.
1. Клонировать или скачать репозиторий;
2. Открыть терминал в PracticumApi;
3. `dotnet build` -> `dotnet run --launch-profile https`;
4. Открыть в браузере `https://localhost:7008/swagger/`.

## Запуск тестов
Для запуска всех тестов выполните команду:
```
dotnet test
```

Для запуска тестов с подробным выводом:
```
dotnet test --verbosity normal
```

## Формат ошибок API

Все ошибки возвращаются в единообразном формате JSON (RFC 7807 - Problem Details):

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 999 не найден",
  "instance": "/events/999"
}
```

### Типы ошибок и статус-коды

| Статус | Название | Описание | Пример сценария |
|--------|----------|---------|-----------------|
| **404** | Resource not found | Ресурс не найден | `GET /events/999` - событие не существует |
| **400** | Validation error | Ошибка валидации данных | `POST /events` с `endAt <= startAt`, `GET /events?page=0` |
| **500** | Internal server error | Ошибка сервера | Непредвиденная ошибка при обработке запроса |

### Примеры ошибок

**404 - Событие не найдено:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 999 не найден",
  "instance": "/events/999"
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
      "id": 1,
      "title": "Conference 2024",
      "description": "Annual tech conference",
      "startAt": "2024-06-15T09:00:00",
      "endAt": "2024-06-15T17:00:00"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10
}
```

### 2. GET /events/{id}
```
curl -X 'GET' \
  'https://localhost:7008/events/1' \
  -H 'accept: text/plain'
```

### 3. POST /events
```
curl -X 'POST' \
  'https://localhost:7008/events' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:48:22.985Z",
  "endAt": "2026-10-04T07:48:22.985Z"
}'
```

### 4. PUT /events/{id}
```
curl -X 'PUT' \
  'https://localhost:7008/events/1' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:54:29.355Z",
  "endAt": "2026-10-04T07:54:29.355Z"
}'
```

### 5. DELETE /events/{id}
```
curl -X 'DELETE' \
  'https://localhost:7008/events/1' \
  -H 'accept: */*'
```

---

## Архитектура обработки ошибок

Проект использует единообразную систему обработки ошибок, основанную на собственных исключениях:

### Собственные исключения (`PracticumApi.Exceptions`)

1. **`NotFoundException`** - выбрасывается, когда запрошенный ресурс не существует
   - Пример: `GET /events/999` когда события с ID 999 нет
   - HTTP статус: 404

2. **`ValidationException`** - выбрасывается при ошибках валидации данных
   - Пример: попытка создать событие с `EndAt < StartAt`
   - HTTP статус: 400

### Обработка исключений

**Middleware:** `GlobalExceptionHandlingMiddleware` перехватывает все необработанные исключения и преобразует их в единообразный JSON ответ (RFC 7807 - Problem Details).

**Преимущества:**
- Единый формат ошибок для всех endpoints
- Централизованная обработка ошибок
- Логирование всех ошибок
- Гибкая расширяемость (легко добавить новые типы исключений)
