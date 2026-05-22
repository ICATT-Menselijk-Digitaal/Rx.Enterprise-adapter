# Rx.Enterprise-adapter

Adapter to retrieve information about Zaken in KISS/ITA from Rx.Enterprise.

<img width="500"   alt="Screenshot 2026-05-13 164817" src="https://github.com/user-attachments/assets/dbb481fe-23c6-4ea8-9a7a-a9ae79fd94e9" />

## Authentication

The Rx.Enterprise API uses mutual TLS (mTLS) — every request must include a client certificate issued per environment.

### Getting the certificates & running locally

The `.crt` and `.key` files are stored in **1Password**. Download both files and place them under:

```
src/RxEnterpriseZgwProxy/certs/client.crt
src/RxEnterpriseZgwProxy/certs/client.key
```

These paths are gitignored.

### Calling the API with curl

```bash
curl --cert certs/client.crt \
     --key  certs/client.key \
     -i 'https://rheden.connect.rx-enterprise-acc.nl/api/zaak/2025-000001'
```

### Running the worker locally

Copy the example env file and fill in the values:

```bash
cp src/RxEnterpriseZgwProxy/.env.example src/RxEnterpriseZgwProxy/.env
```

Then run:

```bash
dotnet run --project src/RxEnterpriseZgwProxy
```

## Rx.Enterprise to KISS/ZGW field mapping

The `RxEnterpriseZgwProxy` exposes the subset of ZGW-shaped endpoints that KISS calls. Rx.Enterprise remains the source system; this adapter maps the Rx response fields into the fields KISS reads.

### Call chain

When KISS opens a zaak detail page it makes the following calls in sequence. Each downstream call is driven by a URL embedded in the previous response.

```text
GET /zaken/api/v1/zaken?identificatie=<zaaknummer>   ← user search
  └─ GET /zaken/api/v1/zaken/{id}                    ← detail fetch
       ├─ GET /catalogi/api/v1/zaaktypen/{id}         ← follows $.zaaktype
       ├─ GET /zaken/api/v1/statussen/{id}            ← follows $.status
       │    └─ GET /catalogi/api/v1/statustypen/{id}  ← follows $.statustype
       ├─ GET /zaken/api/v1/rollen?zaak=<url>
       └─ GET /zaken/api/v1/zaakinformatieobjecten?zaak=<url>
            └─ GET /documenten/api/v1/enkelvoudiginformatieobjecten/{id}
```

### Zaak search and detail

```text
GET /zaken/api/v1/zaken?identificatie=<zaaknummer>
GET /zaken/api/v1/zaken/{id}
```

| KISS/ZGW field | Rx.Enterprise field | Notes |
| --- | --- | --- |
| `url` | generated | `/zaken/api/v1/zaken/{sleutel}` |
| `uuid` | `sleutel` | |
| `identificatie` | `sleutel` | |
| `omschrijving` | `betreft` | |
| `bronorganisatie` | none | Empty string. |
| `zaaktype` | `zaaktypesleutel` | URL: `/catalogi/api/v1/zaaktypen/{base64(zaaktypesleutel)}` |
| `registratiedatum` | `boekdatum` | |
| `startdatum` | `boekdatum` | |
| `status` | `afhandelingsstatus` | URL: `/zaken/api/v1/statussen/{base64(afhandelingsstatus)}` |
| `toelichting` | none | Empty string. |

### Rollen

```text
GET /zaken/api/v1/rollen?zaak=<zaak-url>
```

Returns two roles. KISS identifies them by `omschrijvingGeneriek`.

| Role | `omschrijvingGeneriek` | Rx.Enterprise source |
| --- | --- | --- |
| Aanvrager | `initiator` | `voornamenafzender`, `voorvoegselafzender`, `voorlettersafzender` |
| Behandelaar | `behandelaar` | `eerstebehandelaar[0]` → mapped to `geslachtsnaam` |

Both roles use `betrokkeneType: "natuurlijk_persoon"`.

### Status

```text
GET /zaken/api/v1/statussen/{id}
```

The `{id}` is a URL-safe base64 encoding of the `afhandelingsstatus` value from the zaak.

| KISS/ZGW field | Source | Notes |
| --- | --- | --- |
| `url` | generated | Echoes the requested URL. |
| `uuid` | encoded status name | |
| `zaak` | none | `null` |
| `statustype` | generated | `/catalogi/api/v1/statustypen/{id}` |
| `datumStatusGezet` | none | `null` |
| `statustoelichting` | decoded status name | |
| `indicatieLaatstGezetteStatus` | none | `null` |

### Catalogi

```text
GET /catalogi/api/v1/zaaktypen/{id}
GET /catalogi/api/v1/statustypen/{id}
```

The `{id}` segment is always a URL-safe base64 encoding of the original key, written by the adapter when building zaak and status URLs.

**Zaaktypen** — proxied to `GET data/zaaktype/{sleutel}` on Rx.Enterprise:

| KISS/ZGW field | Rx.Enterprise field |
| --- | --- |
| `identificatie` | `sleutel` |
| `omschrijving` | `onderwerp` |

**Statustypen** — resolved locally from the encoded status name:

| KISS/ZGW field | Source |
| --- | --- |
| `url` | generated (echoes the requested URL) |
| `omschrijving` | decoded status name (`afhandelingsstatus`) |
| `omschrijvingGeneriek` | empty string |

### Documents and attachments

```text
GET /zaken/api/v1/zaakinformatieobjecten?zaak=<zaak-url>
GET /documenten/api/v1/enkelvoudiginformatieobjecten/{documentnummer}--{virtualid}
GET /documenten/api/v1/enkelvoudiginformatieobjecten/{documentnummer}--{virtualid}/download
```

For `zaakinformatieobjecten` the adapter fetches the zaak, reads `documentnummer`, then fetches the document metadata from Rx.Enterprise.

| KISS/ZGW field | Rx.Enterprise field | Notes |
| --- | --- | --- |
| `uuid` | `{documentnummer}--{virtualid}` | Stable MD5-based UUID. |
| `informatieobject` | generated | Points to the enkelvoudiginformatieobject endpoint. |
| `zaak` | request `zaak` param | Echoed back. |
| `registratiedatum` | `bijlagedatumtijd[virtualid - 1]` | |
| `aardRelatieWeergave` | none | Empty string. |
| `titel`, `beschrijving` | none | Empty strings. |
| `vernietigingsdatum`, `status` | none | `null` |

For `enkelvoudiginformatieobjecten`:

| KISS/ZGW field | Rx.Enterprise field | Notes |
| --- | --- | --- |
| `url` | generated | Echoes the requested URL. |
| `identificatie` | `documentnummer` | |
| `bronorganisatie` | none | Empty string. |
| `creatiedatum` | `creatiedatum` | |
| `titel` | `bijlageomschrijving[virtualid - 1]` | Falls back to filename. |
| `vertrouwelijkheidaanduiding` | none | Fixed: `openbaar`. |
| `formaat` | `bijlagecontenttype[virtualid - 1]` | Falls back to `application/octet-stream`. |
| `bestandsnaam` | `bijlageinfo[].filename` | Matched by `virtualid`. |
| `bestandsomvang` | `bijlagegrootte[virtualid - 1]` | Parsed as integer; falls back to `0`. |
| `inhoud` | generated | `{url}/download` |
