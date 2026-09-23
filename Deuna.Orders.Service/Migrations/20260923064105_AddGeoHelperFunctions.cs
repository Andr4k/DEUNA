using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Orders.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoHelperFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El esquema `geo` y sus funciones helper son parte del esquema de datos, no del
            // bootstrap del contenedor: initdb.d solo se ejecuta con el volumen vacío, así que
            // un entorno ya inicializado nunca las recibía y las consultas crudas fallaban con
            // "schema geo does not exist". Versionarlas aquí las aplica en cualquier entorno.
            migrationBuilder.Sql(@"
                CREATE SCHEMA IF NOT EXISTS geo;

                CREATE OR REPLACE FUNCTION geo.distance_meters(lat1 float, lon1 float, lat2 float, lon2 float)
                RETURNS float AS $$
                    SELECT ST_Distance(
                        ST_SetSRID(ST_MakePoint(lon1, lat1), 4326)::geography,
                        ST_SetSRID(ST_MakePoint(lon2, lat2), 4326)::geography
                    );
                $$ LANGUAGE sql IMMUTABLE;

                CREATE OR REPLACE FUNCTION geo.point_in_polygon(lat float, lon float, polygon geometry)
                RETURNS boolean AS $$
                    SELECT ST_Contains(polygon, ST_SetSRID(ST_MakePoint(lon, lat), 4326));
                $$ LANGUAGE sql IMMUTABLE;

                GRANT USAGE ON SCHEMA geo TO CURRENT_USER;
                GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA geo TO CURRENT_USER;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS geo.point_in_polygon(float, float, geometry);
                DROP FUNCTION IF EXISTS geo.distance_meters(float, float, float, float);
            ");
        }
    }
}
