# rx-enterprise-adapter

ZGW proxy adapter for Rx.Enterprise — translates Rx.Enterprise case data to ZGW-compatible API responses for KISS-frontend.

![Version: 0.0.0](https://img.shields.io/badge/Version-0.0.0-informational?style=flat-square) ![Type: application](https://img.shields.io/badge/Type-application-informational?style=flat-square) ![AppVersion: 0.0.0](https://img.shields.io/badge/AppVersion-0.0.0-informational?style=flat-square)

## Usage

```bash
helm upgrade --install rx-enterprise-adapter oci://ghcr.io/icatt/rx-enterprise-adapter \
  --set rxEnterprise.baseUrl=https://your-rx-enterprise-url \
  --set rxEnterprise.cert="$(cat client.crt)" \
  --set rxEnterprise.privateKey="$(cat client.key)" \
  --set clients[0].secret=your-secret
```

Mount the cert and key via an existing secret instead:

```bash
helm upgrade --install rx-enterprise-adapter oci://ghcr.io/icatt/rx-enterprise-adapter \
  --set existingSecret=my-rx-enterprise-secret
```

## Values

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| affinity | object | `{}` |  |
| aspnetcoreEnvironment | string | `"Production"` |  |
| clients | list | `[{"id":"rx-adapter","secret":"change-me-min-16-chars"}]` | Clients allowed to call this proxy (must match KISS-frontend ZAAKSYSTEEM_API_CLIENT_ID / ZAAKSYSTEEM_API_KEY) |
| clients[0].secret | string | `"change-me-min-16-chars"` | Shared secret used to sign and verify JWT tokens; minimum 16 characters |
| existingSecret | string | `""` | Name of an existing Secret containing rxEnterprise.cert, rxEnterprise.privateKey and client secrets. When set, the chart will not create a Secret of its own. |
| image.pullPolicy | string | `"IfNotPresent"` |  |
| image.repository | string | `"ghcr.io/icatt-menselijk-digitaal/rx.enterprise-adapter"` |  |
| image.tag | string | `""` |  |
| nodeSelector | object | `{}` |  |
| podAnnotations | object | `{}` |  |
| podSecurityContext.runAsNonRoot | bool | `true` |  |
| podSecurityContext.runAsUser | int | `1000` |  |
| replicaCount | int | `1` |  |
| resources | object | `{}` |  |
| rxEnterprise | object | `{"baseUrl":"https://rheden.connect.rx-enterprise-acc.nl","cert":"","privateKey":""}` | Rx.Enterprise connection settings |
| securityContext.allowPrivilegeEscalation | bool | `false` |  |
| securityContext.capabilities.drop[0] | string | `"ALL"` |  |
| securityContext.readOnlyRootFilesystem | bool | `true` |  |
| service.port | int | `80` |  |
| service.type | string | `"ClusterIP"` |  |
| tolerations | list | `[]` |  |
