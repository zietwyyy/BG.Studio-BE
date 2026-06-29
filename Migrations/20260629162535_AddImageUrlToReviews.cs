using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackgroundRemovalMVP.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.AddColumn<string>(
                    name: "ImageUrl",
                    table: "Reviews",
                    type: "TEXT",
                    nullable: false,
                    defaultValue: "");
            }
            else
            {
                migrationBuilder.AddColumn<string>(
                    name: "ImageUrl",
                    table: "Reviews",
                    type: "text",
                    nullable: false,
                    defaultValue: "");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Reviews");
        }
    }
}
