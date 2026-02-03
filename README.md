# Trackii Inicial

## Configuración rápida

### API (Trackii.Api)
- La cadena de conexión requerida ya está en `Trackii.Api/appsettings.json`.
- Si vas a desplegar en IIS, actualiza la configuración de hosting según tu entorno.

### App Android (Trackii.App)
- La URL base del API se configura en: `Trackii.App/Configuration/AppConfig.cs`.
- Cambia `ApiBaseUrl` por la IP/puerto donde publiques `Trackii.Api` (ej. `http://192.168.0.100:5000`).
- Android ya permite tráfico HTTP (`AndroidUseCleartextTraffic=true`).

## Endpoints principales
- `GET /api/locations` lista localidades activas.
- `POST /api/auth/register` registro con token, usuario, contraseña, localidad y Android ID.
- `POST /api/auth/login` inicio de sesión.
