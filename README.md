# SQL initializer Aspire 13.5.2 hang repro

`AspireUseCliBundle=false`, Aspire **13.5.2**.

```powershell
cd sql-initializer-hang
dotnet run apphost-hang.cs    # hangs at Distributed application starting
dotnet run apphost-fixed.cs  # starts normally
```
