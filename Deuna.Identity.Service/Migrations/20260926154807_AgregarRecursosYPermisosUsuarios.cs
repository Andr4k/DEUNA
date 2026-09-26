using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Identity.Service.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRecursosYPermisosUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "permisos_usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_permisos_usuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_permisos_usuarios_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recursos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Servicio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Clave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Acciones = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recursos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuarios",
                table: "permisos_usuarios",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "ix_permisos_usuarios_unique",
                table: "permisos_usuarios",
                columns: new[] { "UsuarioId", "Permiso", "Recurso", "Accion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recursos_servicio_clave",
                table: "recursos",
                columns: new[] { "Servicio", "Clave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "permisos_usuarios");

            migrationBuilder.DropTable(
                name: "recursos");
        }
    }
}
