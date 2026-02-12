# 🔧 Quick Start - Database Connection

## Connection Method (3 simple steps)

When running the app, select **"🔧 Build New Connection"** and enter:

```
1️⃣  Server:    .                    (or .\SQLEXPRESS, localhost, IP address)
2️⃣  Database:  TextToSqlTest        (your database name)
3️⃣  User ID:   TextToSqlReader      (SQL Server username)
4️⃣  Password:  @TextToSqlReader!    (user password)
```

✅ **TrustServerCertificate** automatically = `True`

---

## Example Connection String generated:

```
Server=.;Database=TextToSqlTest;User Id=TextToSqlReader;Password=@TextToSqlReader!;TrustServerCertificate=True;
```

But you **DO NOT need to enter this entire line**, just enter each part!

---

## Useful Commands:

- `switch db` - Switch to other database
- `show db` - View current database
- `help` - View all commands

📖 [View detailed guide](./DATABASE_CONNECTION_GUIDE.md)
