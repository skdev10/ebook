DigitalOcean Managed MySQL — CA certificate for SSL (Verify CA)

1. In the DigitalOcean control panel: Databases → your cluster → Connection details.
2. Click "Download CA certificate" and save the file as:
      ca-certificate.crt
   in this Certificates folder (same folder as this README).

3. Alternatively, from a machine with doctl:
      doctl databases get-ca <your-cluster-id> -o text > Certificates/ca-certificate.crt

4. For App Platform / CI without committing the file, set a secret env var:
      Database__SslCaPem
   to the full PEM text (the file contents). Newlines can be literal or escaped as \n.

The app sets MySQL SslMode=VerifyCA when a CA file or PEM is configured.

Do not commit database passwords. Use ConnectionStrings__DefaultConnection via environment variables or User Secrets.

Reference: https://docs.digitalocean.com/products/databases/mysql/how-to/connect/
