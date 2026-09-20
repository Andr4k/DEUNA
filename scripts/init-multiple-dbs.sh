#!/bin/bash
# init-multiple-dbs.sh - Crea múltiples bases de datos en PostgreSQL
# Se ejecuta automáticamente al iniciar el contenedor postgres-identity

set -e
set -u

# Función para crear base de datos si no existe
create_db() {
    local database=$1
    echo "Creando base de datos: $database"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
        SELECT 'CREATE DATABASE $database' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$database')\gexec
EOSQL
}

# Crear bases de datos para cada microservicio
create_db "deuna_identity"
create_db "deuna_orders"
create_db "deuna_delivery"
create_db "deuna_feedback"

echo "✅ Todas las bases de datos creadas/verificadas"