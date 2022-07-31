﻿// <auto-generated />
using System;
using MareSynchronosServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MareSynchronosServer.Migrations
{
    [DbContext(typeof(MareDbContext))]
    [Migration("20220731211419_RenameLowerSnakeCase")]
    partial class RenameLowerSnakeCase
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("MareSynchronosServer.Models.Auth", b =>
                {
                    b.Property<string>("HashedKey")
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)")
                        .HasColumnName("hashed_key");

                    b.Property<string>("UserUID")
                        .HasColumnType("character varying(10)")
                        .HasColumnName("user_uid");

                    b.HasKey("HashedKey")
                        .HasName("pk_auth");

                    b.HasIndex("UserUID")
                        .HasDatabaseName("ix_auth_user_uid");

                    b.ToTable("auth", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.Banned", b =>
                {
                    b.Property<string>("CharacterIdentification")
                        .HasColumnType("text")
                        .HasColumnName("character_identification");

                    b.Property<string>("Reason")
                        .HasColumnType("text")
                        .HasColumnName("reason");

                    b.Property<byte[]>("Timestamp")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("timestamp");

                    b.HasKey("CharacterIdentification")
                        .HasName("pk_banned_users");

                    b.ToTable("banned_users", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.ClientPair", b =>
                {
                    b.Property<string>("UserUID")
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("user_uid");

                    b.Property<string>("OtherUserUID")
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("other_user_uid");

                    b.Property<bool>("AllowReceivingMessages")
                        .HasColumnType("boolean")
                        .HasColumnName("allow_receiving_messages");

                    b.Property<bool>("IsPaused")
                        .HasColumnType("boolean")
                        .HasColumnName("is_paused");

                    b.Property<byte[]>("Timestamp")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("timestamp");

                    b.HasKey("UserUID", "OtherUserUID")
                        .HasName("pk_client_pairs");

                    b.HasIndex("OtherUserUID")
                        .HasDatabaseName("ix_client_pairs_other_user_uid");

                    b.HasIndex("UserUID")
                        .HasDatabaseName("ix_client_pairs_user_uid");

                    b.ToTable("client_pairs", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.FileCache", b =>
                {
                    b.Property<string>("Hash")
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("hash");

                    b.Property<byte[]>("Timestamp")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("timestamp");

                    b.Property<bool>("Uploaded")
                        .HasColumnType("boolean")
                        .HasColumnName("uploaded");

                    b.Property<string>("UploaderUID")
                        .HasColumnType("character varying(10)")
                        .HasColumnName("uploader_uid");

                    b.HasKey("Hash")
                        .HasName("pk_file_caches");

                    b.HasIndex("UploaderUID")
                        .HasDatabaseName("ix_file_caches_uploader_uid");

                    b.ToTable("file_caches", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.ForbiddenUploadEntry", b =>
                {
                    b.Property<string>("Hash")
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("hash");

                    b.Property<string>("ForbiddenBy")
                        .HasColumnType("text")
                        .HasColumnName("forbidden_by");

                    b.Property<byte[]>("Timestamp")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("timestamp");

                    b.HasKey("Hash")
                        .HasName("pk_forbidden_upload_entries");

                    b.ToTable("forbidden_upload_entries", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.User", b =>
                {
                    b.Property<string>("UID")
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("uid");

                    b.Property<string>("CharacterIdentification")
                        .HasColumnType("text")
                        .HasColumnName("character_identification");

                    b.Property<bool>("IsAdmin")
                        .HasColumnType("boolean")
                        .HasColumnName("is_admin");

                    b.Property<bool>("IsModerator")
                        .HasColumnType("boolean")
                        .HasColumnName("is_moderator");

                    b.Property<DateTime>("LastLoggedIn")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_logged_in");

                    b.Property<byte[]>("Timestamp")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("bytea")
                        .HasColumnName("timestamp");

                    b.HasKey("UID")
                        .HasName("pk_users");

                    b.HasIndex("CharacterIdentification")
                        .HasDatabaseName("ix_users_character_identification");

                    b.ToTable("users", (string)null);
                });

            modelBuilder.Entity("MareSynchronosServer.Models.Auth", b =>
                {
                    b.HasOne("MareSynchronosServer.Models.User", "User")
                        .WithMany()
                        .HasForeignKey("UserUID")
                        .HasConstraintName("fk_auth_users_user_temp_id");

                    b.Navigation("User");
                });

            modelBuilder.Entity("MareSynchronosServer.Models.ClientPair", b =>
                {
                    b.HasOne("MareSynchronosServer.Models.User", "OtherUser")
                        .WithMany()
                        .HasForeignKey("OtherUserUID")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_client_pairs_users_other_user_temp_id1");

                    b.HasOne("MareSynchronosServer.Models.User", "User")
                        .WithMany()
                        .HasForeignKey("UserUID")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_client_pairs_users_user_temp_id2");

                    b.Navigation("OtherUser");

                    b.Navigation("User");
                });

            modelBuilder.Entity("MareSynchronosServer.Models.FileCache", b =>
                {
                    b.HasOne("MareSynchronosServer.Models.User", "Uploader")
                        .WithMany()
                        .HasForeignKey("UploaderUID")
                        .HasConstraintName("fk_file_caches_users_uploader_uid");

                    b.Navigation("Uploader");
                });
#pragma warning restore 612, 618
        }
    }
}
