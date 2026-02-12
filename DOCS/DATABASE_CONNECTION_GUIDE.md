# 📊 Database Connection Guide - TextToSqlAgent

## 🎯 Overview

From now on, you **don't need to enter complex connection strings** anymore!

The application will **ask for simple information** one by one at startup:

- ✅ Server name
- ✅ Database name
- ✅ User ID
- ✅ Password

**TrustServerCertificate** is pre-configured = `True` (no need to ask).

---

## 🚀 When Starting the App

When running `dotnet run`, you will see a menu:

```
Choose how to connect:

  📁 My Database (last used)        ← Saved connections
  📁 Production DB

  🔧 Build New Connection (Step-by-Step)   ← NEW - Input step by step
  ✏️  Enter Connection String Manually     ← For pro users
```

### ✨ Option 1: Build New Connection (Recommended)

Select **"🔧 Build New Connection"**, the app will ask step-by-step:

```
🔧 Database Connection Setup
Please enter your database connection details:

1️⃣  Server: .                          ← Enter server name (default: .)
   ✓ Server: .

2️⃣  Database: TextToSqlTest            ← Enter database name
   ✓ Database: TextToSqlTest

3️⃣  User ID: TextToSqlReader           ← Enter username
   ✓ User ID: TextToSqlReader

4️⃣  Password: ********                 ← Enter password (hidden)
   ✓ Password: ********
```

Then display summary:

```
╭─✅ Connection Summary────────────────────╮
│ ╭────────────────────┬────────────────╮ │
│ │ Property           │ Value          │ │
│ ├────────────────────┼────────────────┤ │
│ │ Server             │ .              │ │
│ │ Database           │ TextToSqlTest  │ │
│ │ User ID            │ TextToSqlReader│ │
│ │ Password           │ ********       │ │
│ │ Trust Certificate  │ True (default) │ │
│ ╰────────────────────┴────────────────╯ │
╰──────────────────────────────────────────╯
```

App will ask: **"💾 Save this connection for future use?"**

- Select **Yes** → Name it for quick selection next time
- Select **No** → Use temporarily, don't save

---

### 📁 Option 2: Select from Saved Connections

If you have saved connections previously, just:

1. Select connection from the list
2. Press Enter
3. Done! ✅

---

### ✏️ Option 3: Enter Manually (For Pro Users)

If you already have a connection string, select **"✏️ Enter Connection String Manually"** and paste it in.

---

## 🔄 While Running

### Switch Database

If you want to connect to another database **without restarting the app**:

```
💬 Question #1: switch db
```

App will show the menu again to choose a new database.

### View Current Database

```
💬 Question #1: show db
```

Display:

```
╭─📊 Current Database Connection───────╮
│ Server=., Database=TextToSqlTest     │
╰──────────────────────────────────────╯
```

---

## 📝 Useful Commands

| Command                 | Description               |
| ----------------------- | ------------------------- |
| `help` or `?`           | Show command list         |
| `show db`               | View connected database   |
| `switch db` or `đổi db` | Switch to other database  |
| `index`                 | Index schema to vector DB |
| `clear cache`           | Clear schema cache        |
| `exit` or `quit`        | Exit app                  |

---

## 💡 Real World Examples

### Example 1: Localhost SQL Server

```
1️⃣  Server: .
2️⃣  Database: Northwind
3️⃣  User ID: sa
4️⃣  Password: YourPassword123
```

### Example 2: SQL Express

```
1️⃣  Server: .\SQLEXPRESS
2️⃣  Database: AdventureWorks
3️⃣  User ID: testuser
4️⃣  Password: Test@123
```

### Example 3: Remote Server

```
1️⃣  Server: 192.168.1.100
2️⃣  Database: ProductionDB
3️⃣  User ID: appuser
4️⃣  Password: SecurePass!
```

---

## 🔒 Security

- ✅ Password is **hidden** when typing (shows `********`)
- ✅ When displaying connection, only show **Server & Database**, do not show password
- ✅ Saved connections are stored at: `%AppData%\TextToSqlAgent\saved-connections.json`

---

## ❓ FAQ

**Q: Can I use Windows Authentication?**  
A: Current version only supports SQL Server Authentication (User ID + Password). If you need Windows Auth, use option "✏️ Enter Manually" and input: `Server=.;Database=YourDB;Integrated Security=True;TrustServerCertificate=True;`

**Q: Where is the connections file stored?**  
A: `C:\Users\<YourUsername>\AppData\Roaming\TextToSqlAgent\saved-connections.json`

**Q: Can TrustServerCertificate be disabled?**  
A: Default is always `True` to avoid SSL errors. If need to disable, use "Enter Manually" option.

---

## 🎉 Conclusion

Now connecting to database becomes **extremely simple**:

1. Select "Build New Connection"
2. Enter 4 details: Server, Database, User ID, Password
3. Done!

No need to remember complex connection string format anymore! 🚀
