using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Identity.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_administrador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cargo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Departamento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    JefeDirectoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EsSuperAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoAcceso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfiles_administrador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_perfiles_administrador_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroLicencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TipoLicencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FechaVencimientoLicencia = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FotoPerfilUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Disponible = table.Column<bool>(type: "boolean", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CalificacionPromedio = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    TotalEntregas = table.Column<int>(type: "integer", nullable: false),
                    EntregasCompletadas = table.Column<int>(type: "integer", nullable: false),
                    EntregasCanceladas = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfiles_repartidor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_perfiles_repartidor_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_restaurante",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RazonSocial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Ruc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Direccion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Distrito = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Provincia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Departamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CodigoPostal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EmailContacto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AceptaPedidos = table.Column<bool>(type: "boolean", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    HoraApertura = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraCierre = table.Column<TimeSpan>(type: "interval", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfiles_restaurante", x => x.Id);
                    table.ForeignKey(
                        name: "FK_perfiles_restaurante_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RevokedByIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    ReplacedByToken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auditoria_administrador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilAdministradorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Accion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntidadAfectada = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EntidadId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detalle = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Exitoso = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria_administrador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auditoria_administrador_perfiles_administrador_PerfilAdmini~",
                        column: x => x.PerfilAdministradorId,
                        principalTable: "perfiles_administrador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permisos_administrador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilAdministradorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permiso = table.Column<int>(type: "integer", nullable: false),
                    Recurso = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Accion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Concedido = table.Column<bool>(type: "boolean", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permisos_administrador", x => x.Id);
                    table.ForeignKey(
                        name: "FK_permisos_administrador_perfiles_administrador_PerfilAdminis~",
                        column: x => x.PerfilAdministradorId,
                        principalTable: "perfiles_administrador",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documentos_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UrlArchivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FechaEmision = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaVencimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_repartidor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documentos_repartidor_perfiles_repartidor_PerfilRepartidorId",
                        column: x => x.PerfilRepartidorId,
                        principalTable: "perfiles_repartidor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehiculos_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Marca = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Modelo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Placa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AnioFabricacion = table.Column<int>(type: "integer", nullable: true),
                    NumeroSOAT = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FechaVencimientoSOAT = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NumeroTarjetaPropiedad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    EsPrincipal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehiculos_repartidor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehiculos_repartidor_perfiles_repartidor_PerfilRepartidorId",
                        column: x => x.PerfilRepartidorId,
                        principalTable: "perfiles_repartidor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zonas_cobertura_repartidor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreZona = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zonas_cobertura_repartidor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zonas_cobertura_repartidor_perfiles_repartidor_PerfilRepart~",
                        column: x => x.PerfilRepartidorId,
                        principalTable: "perfiles_repartidor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "horarios_atencion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiaSemana = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HoraInicio = table.Column<TimeSpan>(type: "interval", nullable: false),
                    HoraFin = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Cerrado = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_horarios_atencion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_horarios_atencion_perfiles_restaurante_PerfilRestauranteId",
                        column: x => x.PerfilRestauranteId,
                        principalTable: "perfiles_restaurante",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metodos_pago_restaurante",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Proveedor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    EsPredeterminado = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metodos_pago_restaurante", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metodos_pago_restaurante_perfiles_restaurante_PerfilRestaur~",
                        column: x => x.PerfilRestauranteId,
                        principalTable: "perfiles_restaurante",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zonas_cobertura_restaurante",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerfilRestauranteId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreZona = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CostoEnvio = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PedidoMinimo = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    TiempoEstimadoMinutos = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zonas_cobertura_restaurante", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zonas_cobertura_restaurante_perfiles_restaurante_PerfilRest~",
                        column: x => x.PerfilRestauranteId,
                        principalTable: "perfiles_restaurante",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_admin",
                table: "auditoria_administrador",
                column: "PerfilAdministradorId");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_admin_accion",
                table: "auditoria_administrador",
                column: "Accion");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_admin_fecha",
                table: "auditoria_administrador",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_repartidor",
                table: "documentos_repartidor",
                column: "PerfilRepartidorId");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_repartidor_tipo",
                table: "documentos_repartidor",
                columns: new[] { "PerfilRepartidorId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_horarios_restaurante",
                table: "horarios_atencion",
                column: "PerfilRestauranteId");

            migrationBuilder.CreateIndex(
                name: "ix_horarios_restaurante_dia",
                table: "horarios_atencion",
                columns: new[] { "PerfilRestauranteId", "DiaSemana" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_metodos_pago_restaurante",
                table: "metodos_pago_restaurante",
                column: "PerfilRestauranteId");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_admin_activo",
                table: "perfiles_administrador",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_admin_usuario",
                table: "perfiles_administrador",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_repartidor_activo",
                table: "perfiles_repartidor",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_repartidor_disponible",
                table: "perfiles_repartidor",
                column: "Disponible");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_repartidor_licencia",
                table: "perfiles_repartidor",
                column: "NumeroLicencia",
                unique: true,
                filter: "\"NumeroLicencia\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_repartidor_usuario",
                table: "perfiles_repartidor",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_restaurante_activo",
                table: "perfiles_restaurante",
                column: "Activo");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_restaurante_ruc",
                table: "perfiles_restaurante",
                column: "Ruc",
                unique: true,
                filter: "\"Ruc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_perfiles_restaurante_usuario",
                table: "perfiles_restaurante",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permisos_admin",
                table: "permisos_administrador",
                column: "PerfilAdministradorId");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_admin_unique",
                table: "permisos_administrador",
                columns: new[] { "PerfilAdministradorId", "Permiso", "Recurso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires",
                table: "refresh_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_usuario",
                table: "refresh_tokens",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_active",
                table: "usuarios",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_deleted",
                table: "usuarios",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_phone",
                table: "usuarios",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_placa",
                table: "vehiculos_repartidor",
                column: "Placa",
                unique: true,
                filter: "\"Placa\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_repartidor",
                table: "vehiculos_repartidor",
                column: "PerfilRepartidorId");

            migrationBuilder.CreateIndex(
                name: "ix_zonas_cob_repartidor",
                table: "zonas_cobertura_repartidor",
                column: "PerfilRepartidorId");

            migrationBuilder.CreateIndex(
                name: "ix_zonas_cob_restaurante",
                table: "zonas_cobertura_restaurante",
                column: "PerfilRestauranteId");

            migrationBuilder.CreateIndex(
                name: "ix_zonas_cob_restaurante_nombre",
                table: "zonas_cobertura_restaurante",
                columns: new[] { "PerfilRestauranteId", "NombreZona" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_administrador");

            migrationBuilder.DropTable(
                name: "documentos_repartidor");

            migrationBuilder.DropTable(
                name: "horarios_atencion");

            migrationBuilder.DropTable(
                name: "metodos_pago_restaurante");

            migrationBuilder.DropTable(
                name: "permisos_administrador");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "vehiculos_repartidor");

            migrationBuilder.DropTable(
                name: "zonas_cobertura_repartidor");

            migrationBuilder.DropTable(
                name: "zonas_cobertura_restaurante");

            migrationBuilder.DropTable(
                name: "perfiles_administrador");

            migrationBuilder.DropTable(
                name: "perfiles_repartidor");

            migrationBuilder.DropTable(
                name: "perfiles_restaurante");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
