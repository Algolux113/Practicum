# Practicum
PracticumApi.
1. Клонировать или скачать репозиторий;
2. Открыть терминал в PracticumApi;
3. `dotnet build` -> `dotnet run --launch-profile https`;
4. Открыть в браузере `https://localhost:7008/swagger/`.

PracticumApi - 1 СПРИНТ «Разработка каркаса API».
1. `GET /events`
```
curl -X 'GET' \
  'https://localhost:7008/events' \
  -H 'accept: text/plain';
```
2. `GET /events/{id}`
```
curl -X 'GET' \
  'https://localhost:7008/events/1' \
  -H 'accept: text/plain'
```
3. `POST /events`
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
4. `PUT /events/{id}`
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
5. `DELETE /events/{id}`
```
curl -X 'DELETE' \
  'https://localhost:7008/events/1' \
  -H 'accept: */*'
```
