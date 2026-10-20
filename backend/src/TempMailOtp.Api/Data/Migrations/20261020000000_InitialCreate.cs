using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TempMailOtp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemporaryInboxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AccessTokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProviderName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedProviderSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryInboxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Preview = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TextBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SanitizedHtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HasAttachments = table.Column<bool>(type: "bit", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivedEmails_TemporaryInboxes_InboxId",
                        column: x => x.InboxId,
                        principalTable: "TemporaryInboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DetectedOtps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedEmailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    MatchedPhrase = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetectedOtps_ReceivedEmails_ReceivedEmailId",
                        column: x => x.ReceivedEmailId,
                        principalTable: "ReceivedEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetectedOtps_ReceivedEmailId",
                table: "DetectedOtps",
                column: "ReceivedEmailId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedEmails_InboxId_ProviderMessageId",
                table: "ReceivedEmails",
                columns: new[] { "InboxId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedEmails_InboxId_ReceivedAt",
                table: "ReceivedEmails",
                columns: new[] { "InboxId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryInboxes_ExpiresAt",
                table: "TemporaryInboxes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryInboxes_PublicId",
                table: "TemporaryInboxes",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DetectedOtps");

            migrationBuilder.DropTable(
                name: "ReceivedEmails");

            migrationBuilder.DropTable(
                name: "TemporaryInboxes");
        }
    }
}
