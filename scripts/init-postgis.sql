-- init-postgis.sql - Inicializa PostGIS para Orders Service
-- Se ejecuta automáticamente al iniciar postgres-orders

-- Habilitar PostGIS
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
CREATE EXTENSION IF NOT EXISTS postgis_tiger_geocoder;

-- Verificar instalación
SELECT PostGIS_Version();
SELECT PostGIS_Full_Version();

-- Crear esquema para datos geoespaciales si no existe
CREATE SCHEMA IF NOT EXISTS geo;

-- Función helper para calcular distancia en metros entre dos puntos (WGS84)
CREATE OR REPLACE FUNCTION geo.distance_meters(lat1 float, lon1 float, lat2 float, lon2 float)
RETURNS float AS $$
    SELECT ST_Distance(
        ST_SetSRID(ST_MakePoint(lon1, lat1), 4326)::geography,
        ST_SetSRID(ST_MakePoint(lon2, lat2), 4326)::geography
    );
$$ LANGUAGE sql IMMUTABLE;

-- Función helper para verificar si un punto está dentro de un polígono
CREATE OR REPLACE FUNCTION geo.point_in_polygon(lat float, lon float, polygon geometry)
RETURNS boolean AS $$
    SELECT ST_Contains(polygon, ST_SetSRID(ST_MakePoint(lon, lat), 4326));
$$ LANGUAGE sql IMMUTABLE;

-- Índice espacial helper (ejemplo de uso en tablas)
-- CREATE INDEX idx_delivery_zona_geom ON geo.zonas_entrega USING GIST (geom);

GRANT USAGE ON SCHEMA geo TO deuna_user;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA geo TO deuna_user;

DO $$
BEGIN
    RAISE NOTICE '✅ PostGIS inicializado correctamente para deuna_orders';
END $$;