using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deuna.Delivery.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmacionEnLocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QrCodigo",
                table: "pedidos_disponibles",
                newName: "TokenQrLocal");

            migrationBuilder.AddColumn<string>(
                name: "TokenQrEntrega",
                table: "pedidos_disponibles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaLlegadaLocal",
                table: "asignaciones_repartidor",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenQrEntrega",
                table: "pedidos_disponibles");

            migrationBuilder.DropColumn(
                name: "FechaLlegadaLocal",
                table: "asignaciones_repartidor");

            migrationBuilder.RenameColumn(
                name: "TokenQrLocal",
                table: "pedidos_disponibles",
                newName: "QrCodigo");
        }
    }
}
