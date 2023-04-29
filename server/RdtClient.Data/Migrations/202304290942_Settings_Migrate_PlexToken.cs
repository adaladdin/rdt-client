using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdtClient.Data.Migrations
{
    public partial class Settings_Migrate_PlexToken : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Settings SET SettingId = 'Plex:Token' WHERE SettingId = 'Token'");
            migrationBuilder.Sql("UPDATE Settings SET SettingId = 'Plex:Host' WHERE SettingId = 'Host'");
            migrationBuilder.Sql("UPDATE Settings SET SettingId = 'Plex:LibrariesToRefresh' WHERE SettingId = 'LibrariesToRefresh'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
