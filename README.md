# Tauri Achievement Ladder (Backend)

A small .NET 9 Web API that imports character data from local JSON files and a remote Tauri API, stores snapshots in PostgreSQL and exposes endpoints for retrieving sorted leaderboards.

The frontend application for this project is developed separately and can be found here: [Tauri Achievement Ladder - Frontend](https://github.com/HunorTotBagi/tauri-achievement-ladder-frontend)

## Background & Motivation

The website [ladder.tauriwow.com](https://ladder.tauriwow.com/) previously provided player rankings for the [Tauri World of Warcraft](https://tauriwow.com/) private server, displaying statistics such as achievement points and honorable kills. It allowed players to track their progression and compare their standing across the realm.

After the release of the Legion expansion on 2025.Oct.01, the website stopped updating, leaving rankings outdated despite the introduction of many new achievements. As a result, players no longer had a reliable way to check their current position.

This project aims to recreate and modernize the original ladder system, restoring accurate and up-to-date rankings for the Tauri WoW community.

## Features

- Import and synchronize character data (upsert) from:
  - Local JSON files
  - Remote Tauri API
- Leaderboard endpoints:
  - Sorted by **achievement points**
  - Sorted by **honorable kills**
- PostgreSQL persistence using **EF Core (Npgsql)**
- Swagger UI enabled in Development
- CORS configured for Angular frontend (`http://localhost:4200`)
- Docker Compose support for local development

---

## API Endpoints

| Method | Endpoint | Description |
|------|--------|------------|
| POST | `/api/ladder/import/evermoon` | Import / sync character data |
| GET | `/api/ladder/sorted/achievements` | Paginated ladder by achievement points |
| GET | `/api/ladder/sorted/honorableKills` | Paginated ladder by honorable kills |

**Query parameters**
- `page` (default: 1)
- `pageSize` (default: 100)
- `realm` (optional, e.g. `Evermoon`)

---

## Requirements

- .NET 9 SDK  
- PostgreSQL  
- Docker & Docker Compose *(optional)*
