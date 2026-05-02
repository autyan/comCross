# Signing Keys

> Documentation generation notice: this document was drafted with AI
> assistance. Treat security-sensitive content as requiring human review before
> public release.

This document records the public signing material currently committed for
ComCross and the private key storage policy.

## Private Key Policy

Private keys must never be committed to this repository.

The initial private signing material was generated into an operator-controlled
LUKS encrypted container mounted at:

```text
/home/autyan/Secure/comcross-keys
```

Private files are stored under:

```text
private/
```

Public files exported from the same key material are stored in this repository
under:

```text
security/keys/
```

## Public Keys And Certificates

### Release Artifact Signing

Purpose:

- sign `SHA256SUMS`;
- provide a release verification path for downloaded artifacts.

Public key:

```text
security/keys/comcross-release-2026.pub.asc
```

Key id:

```text
comcross-release-2026
```

GPG fingerprint:

```text
9C9D41ADD0B59D863C8B8334675B54E96C7096C4
```

Private key location in the encrypted container:

```text
private/comcross-release-2026.private.asc
```

### Official Plugin Package Signing

Purpose:

- sign official ComCross plugin packages;
- allow the ComCross runtime to distinguish official plugin packages from
  unsigned or tampered runtime plugin packages.

Public key:

```text
security/keys/comcross-plugin-official-2026.pub.pem
```

Key id:

```text
comcross-plugin-official-2026
```

Target algorithm:

```text
RSA-PSS-SHA256
```

Public key SHA-256:

```text
fd670353190330168470f9660ebb32fc0646af290aae219126ef2164621ffa61
```

Private key location in the encrypted container:

```text
private/comcross-plugin-official-2026.private.pem
```

### Windows Self-Signed Test Code Signing

Purpose:

- local Windows MSI/EXE signing smoke tests;
- pre-public self-signed package signing only.

This is not a public-trust Windows code-signing certificate. Windows does not
trust it by default. It does not provide Microsoft or CA-backed publisher
reputation.

Public certificate:

```text
security/keys/comcross-windows-test-codesign-2026.cer.pem
```

Key id:

```text
comcross-windows-test-codesign-2026
```

Certificate SHA-256 fingerprint:

```text
0F:C9:CC:A3:12:09:F7:91:44:79:12:50:C4:55:0C:44:38:B3:12:2E:FD:04:52:CC:6E:A7:FA:20:AB:AE:14:F8
```

Private material in the encrypted container:

```text
private/comcross-windows-test-codesign-2026.private.key.pem
private/comcross-windows-test-codesign-2026.private.pfx
```

The PFX password is stored only in the encrypted container fingerprint record.
Do not commit it.

## Verification Examples

Import the release public key:

```bash
gpg --import security/keys/comcross-release-2026.pub.asc
```

Verify a signed checksum file:

```bash
gpg --verify SHA256SUMS.asc SHA256SUMS
sha256sum -c SHA256SUMS
```

Check the plugin signing public key hash:

```bash
sha256sum security/keys/comcross-plugin-official-2026.pub.pem
```

Inspect the self-signed Windows test certificate:

```bash
openssl x509 -in security/keys/comcross-windows-test-codesign-2026.cer.pem \
  -noout -subject -issuer -fingerprint -sha256
```

## Rotation Notes

- Signing key ids include the year and purpose.
- New public keys may be added before old keys are retired.
- Plugin signatures must carry `keyId` so the runtime can select the correct
  trusted public key.
- Release artifact signatures and plugin package signatures must remain
  separate trust domains.
