using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Deuna.Orders.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrdersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "direcciones_entrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Calle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Numero = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Interior = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Departamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CodigoPostal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Ubicacion = table.Column<Point>(type: "geography (point)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_direcciones_entrega", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "restaurantes_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RazonSocial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Nit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DireccionSede = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric(10,8)", nullable: false),
                    Longitud = table.Column<decimal>(type: "numeric(11,8)", nullable: false),
                    Ubicacion = table.Column<Point>(type: "geography (point)", nullable: false),
                    Distrito = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Provincia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Departamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RadioCoberturaKm = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    AceptaPedidos = table.Column<bool>(type: "boolean", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    HoraApertura = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraCierre = table.Column<TimeSpan>(type: "interval", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurantes_replicados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "horarios_atencion_replicados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteReplicadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaSemana = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Cerrado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_horarios_atencion_replicados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_horarios_atencion_replicados_restaurantes_replicados_Restau~",
                        column: x => x.RestauranteReplicadoId,
                        principalTable: "restaurantes_replicados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CostoEnvio = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DireccionEntregaId = table.Column<Guid>(type: "uuid", nullable: false),
                    QrCodigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NotasCliente = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NotasRestaurante = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaConfirmacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaPreparacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaListo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaEntrega = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaCancelacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pedidos_direcciones_entrega_DireccionEntregaId",
                        column: x => x.DireccionEntregaId,
                        principalTable: "direcciones_entrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_restaurantes_replicados_RestauranteId",
                        column: x => x.RestauranteId,
                        principalTable: "restaurantes_replicados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zonas_cobertura_replicadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestauranteReplicadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreZona = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CostoEnvio = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PedidoMinimo = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    TiempoEstimadoMinutos = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    Geom = table.Column<Polygon>(type: "geography (polygon)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zonas_cobertura_replicadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zonas_cobertura_replicadas_restaurantes_replicados_Restaura~",
                        column: x => x.RestauranteReplicadoId,
                        principalTable: "restaurantes_replicados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contactos_pedido",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contactos_pedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contactos_pedido_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "items_pedido",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreProducto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items_pedido", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_pedido_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tarifas_aplicadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PedidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoTarifa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CostoBase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CostoPorKm = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DistanciaKm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    CostoAdicional = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DetalleCalculo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalCalculado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarifas_aplicadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tarifas_aplicadas_pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contactos_pedido_PedidoId",
                table: "contactos_pedido",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_direcciones_entrega_Ubicacion",
                table: "direcciones_entrega",
                column: "Ubicacion")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_horarios_atencion_replicados_RestauranteReplicadoId",
                table: "horarios_atencion_replicados",
                column: "RestauranteReplicadoId");

            migrationBuilder.CreateIndex(
                name: "IX_items_pedido_PedidoId",
                table: "items_pedido",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_ClienteId",
                table: "pedidos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_Codigo",
                table: "pedidos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_DireccionEntregaId",
                table: "pedidos",
                column: "DireccionEntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_Estado",
                table: "pedidos",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_RestauranteId",
                table: "pedidos",
                column: "RestauranteId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Activo",
                table: "restaurantes_replicados",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Ciudad",
                table: "restaurantes_replicados",
                column: "Ciudad");

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Nit",
                table: "restaurantes_replicados",
                column: "Nit",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurantes_replicados_Ubicacion",
                table: "restaurantes_replicados",
                column: "Ubicacion")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_tarifas_aplicadas_PedidoId",
                table: "tarifas_aplicadas",
                column: "PedidoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_zonas_cobertura_replicadas_Geom",
                table: "zonas_cobertura_replicadas",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_zonas_cobertura_replicadas_RestauranteReplicadoId",
                table: "zonas_cobertura_replicadas",
                column: "RestauranteReplicadoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contactos_pedido");

            migrationBuilder.DropTable(
                name: "horarios_atencion_replicados");

            migrationBuilder.DropTable(
                name: "items_pedido");

            migrationBuilder.DropTable(
                name: "tarifas_aplicadas");

            migrationBuilder.DropTable(
                name: "zonas_cobertura_replicadas");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropTable(
                name: "direcciones_entrega");

            migrationBuilder.DropTable(
                name: "restaurantes_replicados");
        }
    }
}
