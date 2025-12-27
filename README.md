AchievementLadder API

Basic ASP.NET Core Web API exposing ladder endpoints and using Postgres (docker-compose provided).

Endpoints:
- GET /api/ladder?limit=100 -> latest ladder
- POST /api/ladder/snapshot -> accept simple payload to save snapshot

Run:
- Start Postgres: `docker-compose up -d`
- Update `appsettings.json` connection string if needed
- `dotnet run` in `AchievementLadder` project
