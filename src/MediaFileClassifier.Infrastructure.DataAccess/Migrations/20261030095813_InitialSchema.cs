using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFileClassifier.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    parent_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_collection_parent_id",
                        column: x => x.parent_id,
                        principalTable: "collection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "library",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    modified_at = table.Column<long>(type: "INTEGER", nullable: false),
                    schema_version = table.Column<int>(type: "INTEGER", nullable: false),
                    settings = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_registration",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", nullable: false),
                    model_name = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<string>(type: "TEXT", nullable: true),
                    file_hash = table.Column<string>(type: "TEXT", nullable: true),
                    embedding_dimension = table.Column<int>(type: "INTEGER", nullable: true),
                    registered_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_registration", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_search",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    query_text = table.Column<string>(type: "TEXT", nullable: false),
                    filters_json = table.Column<string>(type: "TEXT", nullable: true),
                    sort = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_search", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indexed_folder",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    library_id = table.Column<string>(type: "TEXT", nullable: false),
                    display_path = table.Column<string>(type: "TEXT", nullable: false),
                    platform_handle = table.Column<string>(type: "TEXT", nullable: false),
                    rules = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    added_at = table.Column<long>(type: "INTEGER", nullable: false),
                    status_changed_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexed_folder", x => x.id);
                    table.ForeignKey(
                        name: "FK_indexed_folder_library_library_id",
                        column: x => x.library_id,
                        principalTable: "library",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_item",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    folder_id = table.Column<string>(type: "TEXT", nullable: false),
                    path = table.Column<string>(type: "TEXT", nullable: false),
                    media_type = table.Column<int>(type: "INTEGER", nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    file_created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    file_modified_at = table.Column<long>(type: "INTEGER", nullable: false),
                    imported_at = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    status_reason = table.Column<string>(type: "TEXT", nullable: true),
                    status_changed_at = table.Column<long>(type: "INTEGER", nullable: false),
                    review_status = table.Column<int>(type: "INTEGER", nullable: false),
                    is_favorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    hashes_content_hash = table.Column<string>(type: "TEXT", nullable: true),
                    hashes_fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    hashes_perceptual_hash = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_item_indexed_folder_folder_id",
                        column: x => x.folder_id,
                        principalTable: "indexed_folder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_job",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    folder_id = table.Column<string>(type: "TEXT", nullable: true),
                    item_ids = table.Column<string>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    started_at = table.Column<long>(type: "INTEGER", nullable: true),
                    finished_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processing_job", x => x.id);
                    table.ForeignKey(
                        name: "FK_processing_job_indexed_folder_folder_id",
                        column: x => x.folder_id,
                        principalTable: "indexed_folder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "analysis_run",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    analyzer = table.Column<string>(type: "TEXT", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    model_version = table.Column<string>(type: "TEXT", nullable: true),
                    prompt_version = table.Column<string>(type: "TEXT", nullable: true),
                    started_at = table.Column<long>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<long>(type: "INTEGER", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    rejected_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_analysis_run_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_analysis_run_model_registration_model_id",
                        column: x => x.model_id,
                        principalTable: "model_registration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_membership",
                columns: table => new
                {
                    collection_id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    added_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_membership", x => new { x.collection_id, x.item_id });
                    table.ForeignKey(
                        name: "FK_collection_membership_collection_collection_id",
                        column: x => x.collection_id,
                        principalTable: "collection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_collection_membership_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "keyframe",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    index = table.Column<int>(type: "INTEGER", nullable: false),
                    timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    frame_number = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_keyframe", x => x.id);
                    table.ForeignKey(
                        name: "FK_keyframe_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_rejection",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    tag_key = table.Column<string>(type: "TEXT", nullable: false),
                    tag = table.Column<string>(type: "TEXT", nullable: false),
                    rejected_at = table.Column<long>(type: "INTEGER", nullable: false),
                    rejected_from_analyzer = table.Column<string>(type: "TEXT", nullable: true),
                    rejected_from_model_id = table.Column<string>(type: "TEXT", nullable: true),
                    rejected_from_prompt_version = table.Column<string>(type: "TEXT", nullable: true),
                    rejected_from_run_at = table.Column<long>(type: "INTEGER", nullable: true),
                    rejected_from_run_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_rejection", x => new { x.item_id, x.tag_key });
                    table.ForeignKey(
                        name: "FK_tag_rejection_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technical_field_value",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    field = table.Column<int>(type: "INTEGER", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    read_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_field_value", x => new { x.item_id, x.field });
                    table.ForeignKey(
                        name: "FK_technical_field_value_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technical_metadata",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: true),
                    height = table.Column<int>(type: "INTEGER", nullable: true),
                    orientation = table.Column<int>(type: "INTEGER", nullable: false),
                    duration = table.Column<long>(type: "INTEGER", nullable: true),
                    frame_count = table.Column<int>(type: "INTEGER", nullable: true),
                    frame_rate = table.Column<double>(type: "REAL", nullable: true),
                    container_format = table.Column<string>(type: "TEXT", nullable: true),
                    mime_type = table.Column<string>(type: "TEXT", nullable: true),
                    video_codec = table.Column<string>(type: "TEXT", nullable: true),
                    audio_codec = table.Column<string>(type: "TEXT", nullable: true),
                    bit_rate = table.Column<long>(type: "INTEGER", nullable: true),
                    date_taken = table.Column<long>(type: "INTEGER", nullable: true),
                    date_taken_offset = table.Column<long>(type: "INTEGER", nullable: true),
                    camera_make = table.Column<string>(type: "TEXT", nullable: true),
                    camera_model = table.Column<string>(type: "TEXT", nullable: true),
                    lens_model = table.Column<string>(type: "TEXT", nullable: true),
                    raw_metadata_json = table.Column<string>(type: "TEXT", nullable: true),
                    extracted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    location_altitude_meters = table.Column<double>(type: "REAL", nullable: true),
                    location_latitude = table.Column<double>(type: "REAL", nullable: true),
                    location_longitude = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technical_metadata", x => x.item_id);
                    table.ForeignKey(
                        name: "FK_technical_metadata_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_field_value",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "TEXT", nullable: false),
                    field = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    confidence = table.Column<double>(type: "REAL", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_field_value", x => new { x.run_id, x.field });
                    table.ForeignKey(
                        name: "FK_ai_field_value_analysis_run_run_id",
                        column: x => x.run_id,
                        principalTable: "analysis_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_field_value_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_field_value_model_registration_model_id",
                        column: x => x.model_id,
                        principalTable: "model_registration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tag_assignment",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    tag = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<int>(type: "INTEGER", nullable: false),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    model_id = table.Column<string>(type: "TEXT", nullable: true),
                    confidence = table.Column<double>(type: "REAL", nullable: true),
                    origin = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    tag_key = table.Column<string>(type: "TEXT", nullable: false),
                    accepted_from_analyzer = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_model_id = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_prompt_version = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_run_at = table.Column<long>(type: "INTEGER", nullable: true),
                    accepted_from_run_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_assignment", x => x.id);
                    table.ForeignKey(
                        name: "FK_tag_assignment_analysis_run_run_id",
                        column: x => x.run_id,
                        principalTable: "analysis_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tag_assignment_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tag_assignment_model_registration_model_id",
                        column: x => x.model_id,
                        principalTable: "model_registration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_field_value",
                columns: table => new
                {
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    field = table.Column<int>(type: "INTEGER", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    suggestion_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_analyzer = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_model_id = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_prompt_version = table.Column<string>(type: "TEXT", nullable: true),
                    accepted_from_run_at = table.Column<long>(type: "INTEGER", nullable: true),
                    accepted_from_run_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_field_value", x => new { x.item_id, x.field });
                    table.ForeignKey(
                        name: "FK_user_field_value_analysis_run_suggestion_run_id",
                        column: x => x.suggestion_run_id,
                        principalTable: "analysis_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_field_value_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "embedding",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<string>(type: "TEXT", nullable: false),
                    model_id = table.Column<string>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    keyframe_id = table.Column<string>(type: "TEXT", nullable: true),
                    dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    vector = table.Column<byte[]>(type: "BLOB", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding", x => x.id);
                    table.ForeignKey(
                        name: "FK_embedding_keyframe_keyframe_id",
                        column: x => x.keyframe_id,
                        principalTable: "keyframe",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_embedding_media_item_item_id",
                        column: x => x.item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_embedding_model_registration_model_id",
                        column: x => x.model_id,
                        principalTable: "model_registration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_field_value_item_id",
                table: "ai_field_value",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_field_value_model_id",
                table: "ai_field_value",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_item_id_analyzer",
                table: "analysis_run",
                columns: new[] { "item_id", "analyzer" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_run_model_id",
                table: "analysis_run",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_parent_id",
                table: "collection",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_membership_item_id",
                table: "collection_membership",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_embedding_item_id",
                table: "embedding",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_embedding_keyframe_id",
                table: "embedding",
                column: "keyframe_id");

            migrationBuilder.CreateIndex(
                name: "IX_embedding_model_id_item_id",
                table: "embedding",
                columns: new[] { "model_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_indexed_folder_library_id",
                table: "indexed_folder",
                column: "library_id");

            migrationBuilder.CreateIndex(
                name: "IX_keyframe_item_id_index",
                table: "keyframe",
                columns: new[] { "item_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_item_folder_id_path",
                table: "media_item",
                columns: new[] { "folder_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_job_folder_id",
                table: "processing_job",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "IX_processing_job_state",
                table: "processing_job",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_tag_assignment_item_id",
                table: "tag_assignment",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "IX_tag_assignment_model_id",
                table: "tag_assignment",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_tag_assignment_run_id",
                table: "tag_assignment",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_tag_assignment_tag_key",
                table: "tag_assignment",
                column: "tag_key");

            migrationBuilder.CreateIndex(
                name: "IX_user_field_value_suggestion_run_id",
                table: "user_field_value",
                column: "suggestion_run_id");

            // Hand-written: EF Core can't index columns of a complex type. Duplicate detection (E12) and rescans
            // (E01-S06) look items up by hash.
            migrationBuilder.Sql("CREATE INDEX \"IX_media_item_hashes_content_hash\" ON \"media_item\" (\"hashes_content_hash\");");
            migrationBuilder.Sql("CREATE INDEX \"IX_media_item_hashes_fingerprint\" ON \"media_item\" (\"hashes_fingerprint\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_field_value");

            migrationBuilder.DropTable(
                name: "collection_membership");

            migrationBuilder.DropTable(
                name: "embedding");

            migrationBuilder.DropTable(
                name: "processing_job");

            migrationBuilder.DropTable(
                name: "saved_search");

            migrationBuilder.DropTable(
                name: "tag_assignment");

            migrationBuilder.DropTable(
                name: "tag_rejection");

            migrationBuilder.DropTable(
                name: "technical_field_value");

            migrationBuilder.DropTable(
                name: "technical_metadata");

            migrationBuilder.DropTable(
                name: "user_field_value");

            migrationBuilder.DropTable(
                name: "collection");

            migrationBuilder.DropTable(
                name: "keyframe");

            migrationBuilder.DropTable(
                name: "analysis_run");

            migrationBuilder.DropTable(
                name: "media_item");

            migrationBuilder.DropTable(
                name: "model_registration");

            migrationBuilder.DropTable(
                name: "indexed_folder");

            migrationBuilder.DropTable(
                name: "library");
        }
    }
}
