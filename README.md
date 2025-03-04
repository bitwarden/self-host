# Self-Host Release Repository
Bitwarden's dedicated self-hosted release repository. This holds the installation scripts and container images for our self-host project.

For any issues regarding a specific client application, please visit [bitwarden/clients](https://github.com/bitwarden/clients)

Bitwarden provides OCI-Compliant container images in our ghcr.io [repository](https://github.com/orgs/bitwarden/packages?repo_name=self-host).

All images published to the GitHub Container Registry are
signed by using Cosign's [keyless signing](https://docs.sigstore.dev/cosign/signing/overview/). The images can be
verified with the following command (where `$IMAGE_NAME` is set to an image in our repository):
```sh
$ cosign verify ghcr.io/bitwarden/$IMAGE_NAME:latest --certificate-identity-regexp="https://github\.com/bitwarden/self-host/\.github/workflows/release\.yml@.*" --certificate-oidc-issuer="https://token.actions.githubusercontent.com"
```

---
[![Deploy to DO](https://www.deploytodo.com/do-btn-blue.svg)](https://marketplace.digitalocean.com/apps/bitwarden?action=deploy)

[Deploy to Kubernetes with Helm](https://github.com/bitwarden/helm-charts/blob/main/charts/self-host/README.md)
