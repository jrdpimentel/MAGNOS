# Google sign-in for the member portal (optional)

Google only allows sign-in from a secure (**HTTPS**) address, so a plain `http://192.168.x.x:5000` address will not work.

1. Make the server reachable over HTTPS, for example with a free Cloudflare Tunnel that points a hostname to `http://localhost:5000`.
2. In the Google Cloud Console create a project, then **APIs & Services > Credentials > Create credentials > OAuth client ID** (type: Web application).
   Add your HTTPS address under **Authorized JavaScript origins**.
3. Copy the **Client ID** into `C:\MAGNOS\Server\appsettings.json`:
   ```json
   "GoogleClientId": "1234567890-abc.apps.googleusercontent.com"
   ```
4. Restart the service: `Restart-Service MAGNOSServer`.
5. Open `https://your-hostname/portal/`. A Google button appears. New Google accounts are created as members; they can set a PC password in the portal ("Change password", leave the old password empty).

Do not expose the admin dashboard to the internet. Only publish the `/portal/` address if you can restrict the rest.

