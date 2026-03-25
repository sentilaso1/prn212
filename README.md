# WorkFlow Pro (SRS v1.4) - ASP.NET Core Razor + MSSQL

## Tech
- ASP.NET Core `net8.0`, Razor Pages + Web API controllers
- EF Core + SQL Server (MSSQL)
- JWT auth (workspace switching bằng claim `workspace_id`)
- SignalR (realtime Kanban)

## Prerequisites
- .NET SDK (net8.0)
- SQL Server (để chạy migrations / database update)

## Configure MSSQL
Chỉnh `WorkFlowPro/appsettings.json`:
- `ConnectionStrings:DefaultConnection`

## Create database (EF Core migrations)
Từ thư mục gốc `project_prn`:
```powershell
dotnet ef migrations add InitialCreate --project WorkFlowPro --startup-project WorkFlowPro --context WorkFlowProDbContext
dotnet ef database update --project WorkFlowPro --startup-project WorkFlowPro --context WorkFlowProDbContext
```

## Run app
```powershell
dotnet run --project WorkFlowPro
```

Sau khi chạy, gọi các endpoint dưới `/api/...` và mở SignalR hub `/hubs/kanban`.

## Git
- Repo đã được `git init` tại thư mục `project_prn`
- `.gitignore` đã loại `bin/obj` và các file config nhạy cảm

