# BTL-Donate-Web

## Docker deployment

The web application runs in the Linux container. SQL Server must be reachable from that container; on a VPS, set `DB_SERVER` to the SQL Server host/IP in `.env` (do not use `host.docker.internal` unless SQL Server runs on the VPS host).

1. Copy `.env.example` to `.env` and replace every placeholder with database credentials and newly issued PayOS credentials.
2. Start the application from this directory with `docker compose up --build -d`.
3. Check `http://localhost:8080/health`. It returns healthy only when the app can connect to SQL Server.
4. Put a host-level HTTPS reverse proxy in front of `127.0.0.1:8080` and configure the PayOS webhook URL as `https://your-domain/api/payment/webhook`. Forward `X-Forwarded-Proto` and support WebSocket upgrades for `/paymentHub`.

The compose file persists ASP.NET Data Protection keys in a named Docker volume. Keep that volume when upgrading or replacing the container so existing login cookies remain valid.

The Windows TTS companion is a separate application for the livestream PC; it connects outbound to the public HTTPS address and is not part of the Linux container. Publish it from Windows with:

```powershell
dotnet publish DonateWeb.TtsCompanion/DonateWeb.TtsCompanion.csproj -c Release -r win-x64 --self-contained true
```

Do not commit `.env`. PayOS credentials were present in the previously uploaded example configuration; revoke and replace them in the PayOS dashboard before deployment.
