using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackgroundRemovalMVP.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PostgreSQL migration code
                migrationBuilder.CreateTable(
                    name: "Reviews",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                        UserId = table.Column<int>(type: "integer", nullable: false),
                        Rating = table.Column<int>(type: "integer", nullable: false),
                        Comment = table.Column<string>(type: "text", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Reviews", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Reviews_Users_UserId",
                            column: x => x.UserId,
                            principalTable: "Users",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Reviews_UserId",
                    table: "Reviews",
                    column: "UserId");

                // Indexes and foreign keys for other tables if not already present
                migrationBuilder.CreateIndex(
                    name: "IX_PaymentOrders_UserId",
                    table: "PaymentOrders",
                    column: "UserId");

                migrationBuilder.AddForeignKey(
                    name: "FK_PaymentOrders_Users_UserId",
                    table: "PaymentOrders",
                    column: "UserId",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
            else
            {
                // SQLite migration code
                migrationBuilder.CreateTable(
                    name: "Reviews",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "INTEGER", nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                        UserId = table.Column<int>(type: "INTEGER", nullable: false),
                        Rating = table.Column<int>(type: "INTEGER", nullable: false),
                        Comment = table.Column<string>(type: "TEXT", nullable: false),
                        CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Reviews", x => x.Id);
                        table.ForeignKey(
                            name: "FK_Reviews_Users_UserId",
                            column: x => x.UserId,
                            principalTable: "Users",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Reviews_UserId",
                    table: "Reviews",
                    column: "UserId");

                migrationBuilder.CreateIndex(
                    name: "IX_PaymentOrders_UserId",
                    table: "PaymentOrders",
                    column: "UserId");

                migrationBuilder.AddForeignKey(
                    name: "FK_PaymentOrders_Users_UserId",
                    table: "PaymentOrders",
                    column: "UserId",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_Users_UserId",
                table: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_UserId",
                table: "PaymentOrders");
        }
    }
}
