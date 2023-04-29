using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdtClient.Data.Migrations
{
    public partial class Settings_Migrate_PlexToken : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Settings SET SettingId = 'Plex:Token' WHERE SettingId = 'Token'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
