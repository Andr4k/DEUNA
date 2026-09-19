-- Inicialización de PostGIS para Orders Database
-- Este script se ejecuta automáticamente al crear el contenedor postgres-orders

-- Habilitar extensión PostGIS para operaciones geoespaciales
CREATE EXTENSION IF NOT EXISTS postgis;

-- Habilitar extensión PostGIS Topology (opcional, pero útil)
CREATE EXTENSION IF NOT EXISTS postgis_topology;

-- Verificar que PostGIS se instaló correctamente
SELECT PostGIS_Version();

-- Crear índice GIST por defecto para columnas geography/geometry
-- (Los índices específicos se crearán en las migraciones de EF Core)

-- Mensaje de confirmación
DO $$
BEGIN
    RAISE NOTICE 'PostGIS extensions initialized successfully for deuna_orders database';
END $$;
