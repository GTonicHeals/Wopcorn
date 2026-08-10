# Hosting Wopcorn on a Tailscale machine

Wopcorn is a private, invite-only app. This setup keeps it that way: the server
listens on `127.0.0.1` and nothing else, and the only way in is through
Tailscale, which terminates HTTPS with a real certificate for the machine's
tailnet name. Nothing is exposed to the internet, no ports are forwarded, and no
Windows firewall rule is needed.

Everything below is done by `Host-Wopcorn.ps1`. Read on if you want to know what
it is doing, or when something goes wrong.

---

## 1. What the host machine needs

| | Why |
|---|---|
| **Windows 10/11**, always on | The script uses Windows scheduled tasks for autostart |
| **.NET 10 SDK** | Builds and publishes the app, and runs the database migrations |
| **Node.js 22.18+ or 24.12+** | Builds the Vue client during publish |
| **Tailscale**, signed in | Provides the HTTPS front door |
| **A clone of this repository** | The script publishes from source; a copy of the build output is not enough, because migrations need the project |

Check the first three at once:

```powershell
dotnet --version   # 10.x
node --version     # v22.18+ or v24.12+
tailscale status   # should list this machine, not "Logged out"
```

### Two things to enable in the Tailscale admin console

Both are one-time, on [login.tailscale.com/admin/dns](https://login.tailscale.com/admin/dns):

1. **MagicDNS** — on.
2. **HTTPS Certificates** — on. This is what lets the machine get a real
   Let's Encrypt certificate for `machine-name.your-tailnet.ts.net`.

Without #2 `tailscale serve --https` fails and the app has no HTTPS front door.
That is not a cosmetic problem — see [§6](#6-why-https-is-not-optional).

---

## 2. First run

```powershell
cd <repo>\deploy
powershell -ExecutionPolicy Bypass -File .\Host-Wopcorn.ps1
```

The first run creates `deploy\wopcorn.host.json` and stops, so you can fill it
in. If you are running this on the machine the app was developed on, the TMDB
credentials are copied out of .NET user secrets automatically and there is
nothing to edit.

```jsonc
{
  "Port": 5080,                                 // loopback port Kestrel binds
  "ServePort": 443,                             // tailnet port
  "DataDir": "C:\\ProgramData\\Wopcorn",        // database, avatars, logs
  "PublishDir": "C:\\ProgramData\\Wopcorn\\app",
  "Tmdb": {
    "ReadAccessToken": "eyJhbGciOi...",         // v4 bearer token — the one to set
    "ApiKey": ""                                // v3 key, only used if there is no token
  },
  "Smtp": {
    "Host": "",                                 // empty = no mail, see §7
    "Port": 587,
    "UseStartTls": true,
    "UserName": "",
    "Password": "",
    "FromAddress": "no-reply@wopcorn.local",
    "FromName": "Wopcorn",
    "AppBaseUrl": ""                            // leave empty
  }
}
```

**Get a TMDB token** at [themoviedb.org](https://www.themoviedb.org/settings/api)
— Settings → API → "API Read Access Token". It is the long `eyJ...` string, not
the short v3 key. Without it the app starts fine and then answers `503` to
every search and every title screen, which is to say it does nothing.

This file holds secrets in plain text. It is gitignored; keep it that way.

Then run it again:

```powershell
.\Host-Wopcorn.ps1
```

That publishes the app (client build included, ~1–2 minutes), applies the
database migrations, starts the server, points `tailscale serve` at it, and
prints the URL. Give that URL to your friends.

---

## 3. Day-to-day commands

All of them are `.\Host-Wopcorn.ps1 <command>`:

| Command | What it does |
|---|---|
| `deploy` | *(default)* stop → publish → migrate → start → serve |
| `deploy -SkipBuild` | Same, without rebuilding — for a config change |
| `start` / `stop` / `restart` | Just the app; the tailnet mapping stays put |
| `status` | What is running, where, and on which URL |
| `logs` | Last 60 lines of the newest log |
| `logs -Follow` | Live tail |
| `serve` / `unserve` | Add or remove the tailnet mapping |
| `install-startup` | Start at boot, and restart after a crash (needs admin) |
| `uninstall-startup` | Undo that |

Any of them accept `-Port`, `-ServePort`, `-DataDir`, `-PublishDir` to override
the settings file for one run.

### Keeping it up across reboots

```powershell
# in an elevated PowerShell
.\Host-Wopcorn.ps1 install-startup
```

This registers a scheduled task named **Wopcorn** running as `SYSTEM`, with two
triggers: one at boot, and one every ten minutes. `start` does nothing when the
app is already healthy, so the ten-minute trigger is a cheap supervisor that
brings the app back if it ever falls over.

The `tailscale serve` mapping does not need any of this — tailscaled stores it
and reapplies it itself on every boot.

One consequence: once the task has started the app as `SYSTEM`, stopping it from
a normal PowerShell window will fail with "access denied". Use an elevated
window for `stop` and `restart`, or `Stop-ScheduledTask -TaskName Wopcorn` first.

### Updating to a newer version of the app

```powershell
git pull
.\Host-Wopcorn.ps1 deploy
```

`deploy` is safe to re-run. It never touches the database except to apply
migrations that have not been applied yet, and avatars are stored outside the
folder it overwrites.

---

## 4. Where everything lives

```
C:\ProgramData\Wopcorn\
├── wopcorn.db          the whole app: accounts, lists, ratings, friendships
├── wopcorn.db-wal      SQLite write-ahead log
├── wopcorn.db-shm
├── avatars\            uploaded profile pictures
├── logs\               one file per start, last 20 kept
├── wopcorn.pid
└── app\                the published build — disposable, recreated every deploy
```

`app\wwwroot\avatars` is a directory junction pointing at `avatars\`. Avatars
are written into the web root at runtime, which would otherwise put user uploads
inside the folder every redeploy overwrites.

### Backups

Everything that matters is `wopcorn.db` and `avatars\`. Nothing else in that
tree is worth keeping. Stop the app first so SQLite's WAL is checkpointed:

```powershell
.\Host-Wopcorn.ps1 stop
$stamp = Get-Date -Format yyyyMMdd
Copy-Item C:\ProgramData\Wopcorn\wopcorn.db "D:\backups\wopcorn-$stamp.db"
Copy-Item C:\ProgramData\Wopcorn\avatars "D:\backups\avatars-$stamp" -Recurse
.\Host-Wopcorn.ps1 start
```

---

## 5. Letting friends in

Wopcorn has its own accounts — anyone who can reach the URL can register. Reaching
the URL is the gate, and that gate is your tailnet. Two ways to open it:

- **Add them to your tailnet.** Admin console → Users → Invite. They install
  Tailscale, sign in, and `https://machine.your-tailnet.ts.net` just works for
  them, on phones included.
- **Share the machine.** Admin console → Machines → the host → Share. They get a
  link that adds only this one machine to their own tailnet. Narrower, and the
  better option for someone who is not otherwise part of your network.

Once in, they register an account in the app, then send each other friend
requests — Wopcorn's social features are all mutual-friends-only, so being on
the tailnet gets you in the door and nothing more.

**On phones**, opening the URL in Safari or Chrome and choosing "Add to Home
Screen" installs it as a standalone app — Wopcorn ships a web manifest and a
service worker for this.

> **Note:** posters and backdrops load from `image.tmdb.org` in the browser, not
> through the server. Devices need ordinary internet access alongside Tailscale,
> which they will have. Only the API goes over the tailnet.

### Do not use Tailscale Funnel

`tailscale funnel` would publish the same URL to the whole internet. Do not.
Registration is open to anyone who can reach the page, so the tailnet boundary
*is* the access control.

---

## 6. Why HTTPS is not optional

Three things in Wopcorn need a genuine secure origin, and all three break
silently over plain HTTP:

- **The auth cookie** is issued with `Secure`, always. A browser on an `http://`
  origin throws it away, so sign-in appears to succeed and the next request is
  unauthenticated.
- **Passkeys.** WebAuthn only runs in a secure context, and it binds each
  credential to one relying-party id derived from the hostname.
- **Service workers**, hence the installable-app behaviour.

`tailscale serve` handles this with a real certificate, which is why it is worth
the two admin-console toggles rather than a self-signed cert.

### The forwarded-headers detail

The script sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the app process.
This matters more than it looks. Tailscale terminates TLS and forwards a plain
HTTP request to Kestrel; without that variable the app believes the request
arrived over `http://` and builds the wrong origin from it. Two visible
consequences:

- Password-reset links come out as `http://machine.ts.net/...` and are rejected.
- WebAuthn origin validation fails, so **every passkey registration and sign-in
  fails**.

Both were verified on this codebase — with the variable set, the app builds
`https://machine.ts.net/...`; without it, `http://`. Do not remove it.

### Passkeys are bound to the hostname

A passkey registered at `https://nubby.tailnet.ts.net` works only there. If you
rename the machine in Tailscale, or move the app to a different host, everyone's
passkeys stop working. They can still sign in with their password — passkeys are
an additional method, never the only one — and re-register a passkey on the new
name. Pick a machine name you are happy with before inviting people.

---

## 7. Password reset mail

With `Smtp.Host` empty, no mail is sent: the app writes the reset link into its
log at `Information` instead. That is a legitimate way to run — nobody needs an
SMTP server for a handful of friends. When someone forgets their password:

```powershell
.\Host-Wopcorn.ps1 logs -Follow
# ask them to use "Forgot password", then copy the link out of the log
```

To send mail for real, fill in `Smtp.Host`, `Port`, `UserName`, `Password` and
`FromAddress`, then `restart`. Leave `AppBaseUrl` empty — with forwarded headers
on, links are built from the tailnet origin the request came in on, which is
right.

`POST /api/auth/forgot-password` always answers `202`, for any address, real or
not. That is deliberate: it must not reveal who has an account.

---

## 8. When something is wrong

**`MSB3027: could not copy ... Wopcorn.Server.exe` during deploy**
The app is still running. The script stops it first, but a copy started outside
the script, or one running as `SYSTEM` under the scheduled task, will not have
been. Stop it and re-run:

```powershell
Stop-ScheduledTask -TaskName Wopcorn -ErrorAction SilentlyContinue
Get-Process Wopcorn.Server -ErrorAction SilentlyContinue | Stop-Process -Force
```

**`SQLite Error 1: 'no such table: ...'` at runtime**
A migration was not applied. `deploy` runs them; `start` does not, and neither
does the app itself at startup. Run `.\Host-Wopcorn.ps1 deploy -SkipBuild`.

**Everything loads but search returns nothing and titles show an error**
No TMDB credentials, or a bad token. `status` warns about this, and the app logs
a warning at startup. Check `Tmdb.ReadAccessToken` in `wopcorn.host.json`, then
`restart`.

**`tailscale serve` fails**
In order of likelihood: HTTPS Certificates not enabled in the admin console;
the command needs an elevated PowerShell; MagicDNS is off.

**`<name>:443 is already served to <something else>`**
Another app on this machine already owns the tailnet's root URL. Either free it
(`tailscale serve --https=443 off`) or give Wopcorn a different port:

```powershell
.\Host-Wopcorn.ps1 serve -ServePort 8443    # https://machine.ts.net:8443
```

Set `ServePort` in `wopcorn.host.json` so it sticks. Remember that changing the
port does not change the hostname, so passkeys keep working.

**The URL works for you and not for a friend**
Check `tailscale status` on their device — they need to be signed into the same
tailnet, or have accepted the machine share. A shared machine has to be
explicitly accepted before it appears.

**The app will not start**
`.\Host-Wopcorn.ps1 logs` shows the newest log, and the failed-start path prints
the last lines of both stdout and stderr automatically.

---

## 9. What the script actually runs

For anyone who would rather do it by hand, or is debugging the script itself:

```powershell
# 1. publish (builds the Vue client and drops it in wwwroot)
dotnet publish Wopcorn.Server\Wopcorn.Server.csproj -c Release -o C:\ProgramData\Wopcorn\app

# 2. migrate — nothing does this at startup, by design
$env:ConnectionStrings__Wopcorn = "Data Source=C:\ProgramData\Wopcorn\wopcorn.db"
dotnet ef database update --project Wopcorn.Server\Wopcorn.Server.csproj `
    --context WopcornDbContext --no-build --configuration Release

# 3. run, on loopback only
$env:ASPNETCORE_ENVIRONMENT              = "Production"
$env:ASPNETCORE_URLS                     = "http://127.0.0.1:5080"
$env:ASPNETCORE_FORWARDEDHEADERS_ENABLED = "true"
$env:Tmdb__ReadAccessToken               = "eyJ..."
C:\ProgramData\Wopcorn\app\Wopcorn.Server.exe

# 4. front it with HTTPS on the tailnet
tailscale serve --bg --https=443 http://127.0.0.1:5080
```

Note that configuration comes in as environment variables. .NET user secrets
only load in the Development environment, so a production host cannot rely on
them — that is why `wopcorn.host.json` exists, and why the script copies the
TMDB credentials out of user secrets on first run when it finds them.
