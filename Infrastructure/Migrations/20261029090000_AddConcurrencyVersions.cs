using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Migrations;

public partial class AddConcurrencyVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "version", table: "events", type: "uuid",
            nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.AddColumn<Guid>(name: "version", table: "bookings", type: "uuid",
            nullable: false, defaultValue: Guid.Empty);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "version", table: "events");
        migrationBuilder.DropColumn(name: "version", table: "bookings");
    }
}
