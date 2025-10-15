# DeviceRegistry.Api – Dokumentation
DeviceRegistry.Api är tjänsten som hanterar **tenants** (organisationer/kunder) och deras **devices** (IoT-enheter). Alla övriga delar av systemet bygger på att registryt är korrekt konfigurerat.

## Översikt
- **Tenants**: Representerar en kund eller ett kontorshotell (t.ex. "Innovia Hub").
- **Devices**: Representerar en sensor/enhet som tillhör en tenant, identifierad med ett unikt `serial`.


## Bas-URL
Som standard kör tjänsten på:
```
http://localhost:5101
```

Swagger finns på:
```
http://localhost:5101/swagger
```

---

## Endpoints

### Skapa tenant
```http
POST /api/tenants
Content-Type: application/json
```

**Body:**
```json

  "name": "Innovia Hub",
  "slug": "innovia"

```
  "id": "44a8ce94-888e-478c-8007-3b28544fdf51",
  "name": "Innovia Hub",
  "slug": "innovia"

```

---

### Lista alla tenants
```http
GET /api/tenants
```

---

### Hämta tenant via slug
```http
GET /api/tenants/by-slug/{slug}
```

**Exempel:**
```
GET /api/tenants/by-slug/innovia
```

---


### Skapa en device
```http
POST /api/tenants/{tenantId}/devices
Content-Type: application/json
```

**Body:**
```json
  "model": "Acme CO2-Temp",
  "serial": "dev-101",
  "status": "active"
```

**Svar (201 Created):**
```json

  "id": "027cd86a-1459-4d15-94aa-91007deee95f",
  "tenantId": "44a8ce94-888e-478c-8007-3b28544fdf51",
  "model": "Acme CO2-Temp",
  "serial": "dev-101",
  "status": "active"

```

---

### Lista devices för en tenant
```http
GET /api/tenants/{tenantId}/devices
```

---

### Hämta device via serial
```http
GET /api/tenants/{tenantId}/devices/by-serial/{serial}
```

**Exempel:**
```
GET /api/tenants/44a8ce94-888e-478c-8007-3b28544fdf51/devices/by-serial/dev-101
```

---

## Användningsflöde

1. **Skapa tenant** (t.ex. "Innovia Hub").
2. **Lägg till devices** under den tenantens `tenantId`.
3. Dessa devices kan sedan ta emot data via **Ingest.Gateway**. Ingest löser tenant+serial → device GUID genom DeviceRegistry.
4. Data blir åtkomligt via **Portal.Adapter** och i realtid via **Realtime.Hub**.

---

## Tips
- **slug** används som publik identifierare för tenant, t.ex. i URL:er och API-anrop från Ingest/Portal.
- **serial** är det ID som en fysisk enhet (Arduino, sensor etc.) skickar in i payload till Ingest.
- **id (GUID)** används internt för att länka data i databasen.


# DeviceRegistry.Api – Documentation

DeviceRegistry.Api is the service responsible for managing **tenants** (organizations/customers) and their **devices** (IoT units). All other parts of the system depend on the registry being correctly configured.

## Overview
- **Tenants**: Represent an organization or business unit (e.g., "Innovia Hub").
- **Devices**: Represent a sensor/device belonging to a tenant, identified by a unique `serial`.

## Base URL
By default, the service runs at:
```
http://localhost:5101
```

Swagger is available at:
```
http://localhost:5101/swagger
```

---

## Endpoints

### Create a tenant
```http
POST /api/tenants
Content-Type: application/json
```

**Body:**
```json
{
  "name": "Innovia Hub",
  "slug": "innovia"
}
```

**Response (201 Created):**
```json
{
  "id": "44a8ce94-888e-478c-8007-3b28544fdf51",
  "name": "Innovia Hub",
  "slug": "innovia"
}
```

---

### List all tenants
```http
GET /api/tenants
```

---

### Get tenant by slug
```http
GET /api/tenants/by-slug/{slug}
```

**Example:**
```
GET /api/tenants/by-slug/innovia
```

---

### Create a device
```http
POST /api/tenants/{tenantId}/devices
Content-Type: application/json
```

**Body:**
```json
{
  "model": "Acme CO2-Temp",
  "serial": "dev-101",
  "status": "active"
}
```

**Response (201 Created):**
```json
{
  "id": "027cd86a-1459-4d15-94aa-91007deee95f",
  "tenantId": "44a8ce94-888e-478c-8007-3b28544fdf51",
  "model": "Acme CO2-Temp",
  "serial": "dev-101",
  "status": "active"
}
```

---

### List devices for a tenant
```http
GET /api/tenants/{tenantId}/devices
```

---

### Get device by serial
```http
GET /api/tenants/{tenantId}/devices/by-serial/{serial}
```

**Example:**
```
GET /api/tenants/44a8ce94-888e-478c-8007-3b28544fdf51/devices/by-serial/dev-101
```

---

## Usage Flow

1. **Create a tenant** (e.g., "Innovia Hub").
2. **Add devices** under that tenant’s `tenantId`.
3. These devices can then receive data through **Ingest.Gateway**. Ingest resolves tenant+serial → device GUID via DeviceRegistry.
4. Data becomes accessible through **Portal.Adapter** and in real-time via **Realtime.Hub**.

---

## Tips
- **slug** is used as a public identifier for the tenant, e.g., in URLs and API calls from Ingest/Portal.
- **serial** is the ID sent by a physical device (Arduino, sensor, etc.) in payloads to Ingest.
- **id (GUID)** is used internally to link data in the database.