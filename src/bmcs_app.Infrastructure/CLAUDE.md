# bmcs_app.Infrastructure

## Database
- Server: 172.16.6.11
- Database: bmcs_db
- UID: sa
- PWD: Sapassword1

## DB接続情報の管理
- 接続文字列は `bin/Debug/bmcs_config.json` で一元管理
- `AppConfig.ConnectionString`（静的プロパティ）経由で取得
- リポジトリに接続文字列を直接書かない

```json
{
  "connectionString": "Server=...;Database=bmcs_db;User Id=sa;Password=...;TrustServerCertificate=True;"
}
```
