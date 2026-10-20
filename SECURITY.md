# Security

- The server speaks plain HTTP and is meant for a trusted local network. Do **not** open port 5000 to the internet.
  To use the member portal or Google sign-in from outside the café, put the server behind HTTPS (for example a Cloudflare Tunnel).
- Keep the **admin key** and **client key** private. Anyone with the admin key controls the whole system.
- Customers must use a **Standard (non-admin)** Windows account. The lock-down is a barrier against casual tampering, not against an administrator.
- Report vulnerabilities privately through GitHub "Security advisories" instead of a public issue.
