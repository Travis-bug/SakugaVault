# Auto-grab the free Oracle A1 instance (capacity retry)

Oracle's free ARM (`VM.Standard.A1.Flex`) is often "out of capacity." Instead of
clicking **Create** by hand, `ops/oci-launch-retry.sh` loops the launch API every
60s until capacity frees up, then prints the new server's public IP.

You only do the one-time setup once; then run the script and walk away.

## 1. Install the OCI CLI (on your Mac)

```bash
brew install oci-cli      # or: bash -c "$(curl -L https://raw.githubusercontent.com/oracle/oci-cli/master/scripts/install/install.sh)"
oci --version
```

## 2. Configure API-key auth

```bash
oci setup config
```

Answer the prompts:
- **Location for config:** accept default (`~/.oci/config`).
- **User OCID:** Console → top-right profile → **My profile** → copy **OCID**.
- **Tenancy OCID:** Console → profile menu → **Tenancy: …** → copy **OCID**.
- **Region:** `ca-toronto-1`.
- **Generate a new key pair:** **Y** → accept the default key location → leave
  passphrase empty.

It writes a public key path at the end (e.g. `~/.oci/oci_api_key_public.pem`).

Now register that public key with Oracle:
- Console → profile → **My profile → API keys → Add API key →
  Paste/Upload public key** → paste the contents of
  `~/.oci/oci_api_key_public.pem` → **Add**.

Test it works:
```bash
oci iam region list >/dev/null && echo "OCI CLI authenticated ✅"
```

## 3. Run the retry script

```bash
cd "<this repo>"
bash ops/oci-launch-retry.sh
```

It auto-discovers your availability domain, the `sakugavault-vcn` public subnet,
and the latest Ubuntu 24.04 ARM image, then retries the launch. Leave the
terminal open — when capacity appears it launches the instance (4 OCPU / 24 GB,
your SSH key already injected) and prints:

```
 Public IP: 140.x.x.x
```

Want a smaller, easier-to-place size while you wait? Override it:
```bash
OCPUS=2 MEM_GB=12 bash ops/oci-launch-retry.sh
```
(Still Always Free, still plenty for the stack; you can resize up later.)

## 4. After it lands
Continue from **Step 3 of `ops/DEPLOY.md`** (SSH in, install Docker, deploy).

> Tip: capacity in busy regions can take minutes to a day. The script is safe to
> leave running for hours. If your Mac sleeps, the loop pauses — run it under
> `caffeinate -i bash ops/oci-launch-retry.sh` to keep going.
