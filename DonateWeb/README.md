    # BTL-Donate-Web

## Docker deployment

The web application runs in the Linux container. SQL Server must be reachable from that container; on a VPS, set `DB_SERVER` to the SQL Server host/IP in `.env` (do not use `host.docker.internal` unless SQL Server runs on the VPS host).

1. Copy `.env.example` to `.env` and replace every placeholder with database credentials and newly issued PayOS credentials.
2. Start the application from this directory with `docker compose up --build -d`.
3. Check `http://localhost:8080/health`. It returns healthy only when the app can connect to SQL Server.
4. Put a host-level HTTPS reverse proxy in front of `127.0.0.1:8080` and configure the PayOS webhook URL as `https://your-domain/api/payment/webhook`. Forward `X-Forwarded-Proto` and support WebSocket upgrades for `/paymentHub`.

The compose file persists ASP.NET Data Protection keys in a named Docker volume. Keep that volume when upgrading or replacing the container so existing login cookies remain valid.

## Google sign-in

Create an OAuth client of type **Web application** in Google Cloud Console. Add the public site origin and this authorized redirect URI:

```text
https://plantsdonate.duckdns.org/signin-google
```

Set `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` in the VPS `.env`, then recreate the web service. Google sign-in only accepts verified email addresses; a new account receives the Viewer role, while a verified email matching an existing DonateWeb account is linked to that account.

## Facebook sign-in

Create a Meta app and configure Facebook Login for Web. Add this exact Valid OAuth Redirect URI:

```text
https://plantsdonate.duckdns.org/signin-facebook
```

Set `FACEBOOK_APP_ID` and `FACEBOOK_APP_SECRET` in the VPS `.env`. Facebook Login requests only the email permission needed to create or link a DonateWeb account. A new account receives the Viewer role; an email matching an existing account is linked to that account. Meta may restrict login to app roles while the app is in development mode; switch it to Live when it is ready for public use.

The Windows TTS companion is a separate application for the livestream PC; it connects outbound to the public HTTPS address and is not part of the Linux container. Publish it from Windows with:

```powershell
dotnet publish DonateWeb.TtsCompanion/DonateWeb.TtsCompanion.csproj -c Release -r win-x64 --self-contained true
```

Do not commit `.env`. PayOS credentials were present in the previously uploaded example configuration; revoke and replace them in the PayOS dashboard before deployment.

## Central-account bank donations for livestreams

The Streamer configuration page generates a static VietQR for the platform's central bank account linked to SePay. The QR leaves the amount blank and prefills `DW{streamer-id}` as the transfer memo. Viewers enter the amount in their bank app, keep that code, and append their message. DonateWeb displays these transfers as `Người ủng hộ ẩn danh`; it does not save the sender's bank name. On a confirmed donation, the app records the gross amount, retains a 10% platform commission, and credits the remaining 90% to the streamer's separate earnings balance for manual settlement; viewer spendable wallet funds are not changed. The same donation commission applies to PayOS and wallet-funded donations. For Shop orders, the streamer receives 70% of the amount actually paid after coupons and the platform retains 30%.

Automatic donation records, OBS alerts, and TTS require the platform's central bank account to be connected to SePay and a SePay webhook configured with API Key authentication:

1. Set `SEPAY_BANK_NAME`, `SEPAY_ACCOUNT_NUMBER`, and `SEPAY_ACCOUNT_NAME` in the VPS `.env` to the central account details connected to SePay.
2. Add a webhook for incoming transfers to `https://your-domain/api/webhook/sepay`; configure authentication as API Key and use the same secret in the app's `SEPAY_API_KEY` environment variable.
3. Let incoming transfers reach this endpoint. DonateWeb ignores transactions without the expected `DW{streamer-id}` memo and checks that the receiving account matches the configured central account.
4. Recreate the web container after changing `.env`. The PayOS checkout/webhook flow remains in place for wallet deposits, shop orders, and the existing online donation form.

Each streamer keeps separate bank details for withdrawal requests. The platform operator receives the bank transfer in the central account and manually pays the streamer's approved withdrawal request. Without the SePay connection and webhook, the QR can still receive money, but DonateWeb cannot know that a transfer arrived or automatically trigger its alert/TTS.
