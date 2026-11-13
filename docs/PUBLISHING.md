# Publish from Rider

The solution includes `tools/publish.ps1`. It builds locally and deploys over SSH to the existing DigitalOcean server at https://157.230.58.33. The sibling `client` directory must remain alongside this `api` repository.

## Rider button (one-time setup)

The preferred setup is now included as shared Run configurations: select **Publish API** in Rider's Run dropdown, or **Publish Client** in WebStorm's Run dropdown, then click the green Run triangle. Open `api/DevHub.sln` in Rider and the sibling `client` folder in WebStorm. Reopen the project if the IDE does not discover the new `.run` file immediately. The bundled Shell scripts plugin must be enabled. These configurations publish only their respective component and do not apply database migrations. WebStorm reuses the deployment script in the sibling API repository; keep both folders together.

The external-tool setup below is an optional alternative.

Open **Settings > Tools > External Tools**, click **+**, and enter:

- Name: `Publish DevHub`
- Program: `powershell.exe`
- Arguments: `-NoProfile -ExecutionPolicy Bypass -File "$ProjectFileDir$\tools\publish.ps1"`
- Working directory: `$ProjectFileDir$`

These macros assume Rider has `api/DevHub.sln` open. If your Rider project root differs, use the absolute script path instead.

Run **Tools > External Tools > Publish DevHub**. You can assign a shortcut under **Settings > Keymap > External Tools**. Save your files first: publishing builds the current working tree, including uncommitted edits. No commit or push is required.

## Rider terminal

From the API solution directory:

```powershell
.\tools\publish.ps1                 # API and client
.\tools\publish.ps1 -Target Api     # API only
.\tools\publish.ps1 -Target Client  # client only
.\tools\publish.ps1 -BuildOnly      # build without contacting the server
```

When the model has new EF migrations, review them and ensure a usable Azure database restore point before explicitly including them:

```powershell
.\tools\publish.ps1 -Target Api -Migrate
```

Normal publishing does not change the database schema. A migration failure prevents switching to the new application. An application rollback does not reverse schema changes; migrations must remain compatible with the prior release if rollback is needed.

## Authentication and behavior

The Windows SSH agent must contain your unlocked key. If publishing reports an SSH authentication error, run this in Rider's terminal and enter the key passphrase:

```powershell
ssh-add "$env:USERPROFILE\.ssh\id_ed25519"
```

Production database credentials, JWT/chat encryption keys, and HTTPS settings stay on the server. The script never requests the SQL password. API restarts cause a brief interruption. Client-only publishing does not restart the API.

The Run output prints each stage: build, package, upload, activation, and HTTPS check. **Build succeeded** is only the local build; deployment is complete when **Published successfully** appears and the process exits with code 0. The API archive is about 52 MB and can take a few minutes to upload. Remote SSH commands detach from console input; keepalive checks terminate unresponsive connections instead of leaving them hung indefinitely.

The client is built with same-origin `/api` URLs and social login enabled. Use https://dev-hub.site for Google/GitHub login; production credentials remain on the server.

Build failures stop before uploading. Remote deployment is serialized with a lock, stages files before stopping the service, and retains the preceding API/client directories for recovery. Startup and HTTPS failures trigger application-file rollback. SSH interruption may require checking the service manually. Release folders accumulate under `/opt/devhub/releases`; inspect disk usage and retain only needed old releases as part of maintenance.

Requirements: .NET 11 SDK, Node/npm, Windows OpenSSH, and tar on PATH. The self-contained API includes the runtime. The Linux host must support the .NET 11 x64 CPU baseline.

If the .NET 11 SDK is installed in your user profile, put its installation folder first on PATH before starting Rider or running the script. Check the selected SDK with `dotnet --version`.

## Status

```powershell
ssh root@157.230.58.33 "systemctl status devhub --no-pager"
ssh root@157.230.58.33 "journalctl -u devhub -n 50 --no-pager"
```

Rider documentation: https://www.jetbrains.com/help/rider/Configuring_Third-Party_Tools.html

## Publish the admin and docs sites

At the DNS provider for `dev-hub.site`, add DNS-only A records for `admin` and `docs` pointing to `157.230.58.33`. The server needs Certbot and a registered Let's Encrypt account. If the Ubuntu Droplet has no Certbot installation, install the `certbot` package and use `certbot register` on the Droplet to create the account. The deployment uses HTTP validation on port 80 and then enables HTTPS on both subdomains.

From PowerShell in the API directory, run the first deployment with your existing SSH key unlocked in the Windows SSH agent.

```powershell
.\tools\publish-subsites.ps1 -Configure
```

Sign in to Zevont and open [your account profile endpoint](https://dev-hub.site/api/users/me) to find the account GUID. Enter the GUID when PowerShell prompts for it. Use a Zevont account with an email and password to sign in to the admin site. The configured account can view platform totals and the 12 newest users, organizations, and bug reports. The first deployment publishes the API too, which briefly restarts that service. Admin access uses server configuration and requires no database migration.

Use **Publish Admin** or **Publish Docs** in WebStorm for later frontend updates, or run:

```powershell
.\tools\publish-subsites.ps1 -Target Admin
.\tools\publish-subsites.ps1 -Target Docs
.\tools\publish-subsites.ps1 -BuildOnly
```

For a local admin session, set `PlatformAdmin__UserIds__0` to an approved local account GUID in the PowerShell session that starts the API. Run `npm run dev:admin` from the client and open `http://localhost:5174`. The Vite server proxies API requests to the local HTTPS API.

Clone [ZevontDocs](https://github.com/rZulan/ZevontDocs) into a `docs` folder alongside `api` and `client`. **Publish Docs** builds that checkout when it is present. Workspaces without a standalone checkout use the guides in `client/sites/docs`.

