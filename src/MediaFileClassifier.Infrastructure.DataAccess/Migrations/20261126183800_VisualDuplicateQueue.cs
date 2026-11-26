using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Library schema 21: the visual near-duplicate queue (MFC-828, E12-S02). Hand-written: <c>duplicate_dirty_item</c>
    /// lists the items whose visual groups must be checked again, and triggers fill it in the same transaction as the
    /// change: an item is added with hashes, its perceptual hash, content hash or Missing/Excluded status changed, its whole-item embedding or
    /// its keyframes were written or removed, or it's deleted. A change of an item also queues the items with the same
    /// content hash, because exact copies share one place in a visual group, and a deleted item queues its visual
    /// group's other members. Queuing again moves an item to the end (a new <c>seq</c>), so a check that read it
    /// earlier never takes a later change off the queue. Every item with a perceptual hash or keyframes is queued, so
    /// an upgraded library gets its visual groups on first open.
    /// </summary>
    public partial class VisualDuplicateQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "duplicate_dirty_item" (
                    "seq" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "item_id" TEXT NOT NULL UNIQUE);
                """);

            // Status 4 is Missing and 5 Excluded, as in schema 20's triggers.
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_item_insert" AFTER INSERT ON "media_item"
                WHEN new."hashes_perceptual_hash" IS NOT NULL OR new."hashes_content_hash" IS NOT NULL
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id") SELECT new."id" WHERE new."hashes_perceptual_hash" IS NOT NULL;
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id")
                    SELECT "id" FROM "media_item" WHERE "id" <> new."id" AND "hashes_content_hash" = new."hashes_content_hash";
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_item_update" AFTER UPDATE OF "hashes_perceptual_hash", "hashes_content_hash", "status" ON "media_item"
                WHEN new."hashes_perceptual_hash" IS NOT old."hashes_perceptual_hash"
                    OR new."hashes_content_hash" IS NOT old."hashes_content_hash"
                    OR (new."status" IN (4, 5)) IS NOT (old."status" IN (4, 5))
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id") VALUES (new."id");
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id")
                    SELECT "id" FROM "media_item"
                    WHERE "id" <> new."id"
                        AND ("hashes_content_hash" = old."hashes_content_hash" OR "hashes_content_hash" = new."hashes_content_hash");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_item_delete" BEFORE DELETE ON "media_item"
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id")
                    SELECT DISTINCT "m"."item_id" FROM "duplicate_group_member" AS "m"
                    JOIN "duplicate_group" AS "g" ON "g"."id" = "m"."group_id"
                    WHERE "g"."type" = 1 AND "m"."item_id" <> old."id"
                        AND "m"."group_id" IN (SELECT "group_id" FROM "duplicate_group_member" WHERE "item_id" = old."id");
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id")
                    SELECT "id" FROM "media_item" WHERE "id" <> old."id" AND "hashes_content_hash" = old."hashes_content_hash";
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_embedding_insert" AFTER INSERT ON "embedding"
                WHEN new."keyframe_id" IS NULL
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id") VALUES (new."item_id");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_embedding_delete" AFTER DELETE ON "embedding"
                WHEN old."keyframe_id" IS NULL
                    AND EXISTS (SELECT 1 FROM "media_item" WHERE "id" = old."item_id")
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id") VALUES (old."item_id");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "visual_duplicate_keyframe_insert" AFTER INSERT ON "keyframe"
                WHEN new."perceptual_hash" IS NOT NULL
                BEGIN
                    INSERT OR REPLACE INTO "duplicate_dirty_item" ("item_id") VALUES (new."item_id");
                END;
                """);
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "duplicate_dirty_item" ("item_id")
                SELECT "id" FROM "media_item" WHERE "hashes_perceptual_hash" IS NOT NULL
                UNION
                SELECT DISTINCT "item_id" FROM "keyframe" WHERE "perceptual_hash" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_item_insert";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_item_update";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_item_delete";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_embedding_insert";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_embedding_delete";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "visual_duplicate_keyframe_insert";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "duplicate_dirty_item";""");
            migrationBuilder.Sql("""DELETE FROM "duplicate_group" WHERE "type" = 1;""");
        }
    }
}
