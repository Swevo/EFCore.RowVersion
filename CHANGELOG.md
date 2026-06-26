# Changelog

All notable changes to `Swevo.EFCore.RowVersion` will be documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-06-26

### Added
- `[Optimistic]` attribute for marking entities for optimistic-concurrency generation
- Source generator that emits `[Timestamp] byte[] RowVersion` property and `IOptimisticEntity` implementation on decorated partial classes
- `AddOptimisticConcurrencyConfiguration()` extension on `ModelBuilder` — calls `IsConcurrencyToken()` for every `IOptimisticEntity` entity
- `SaveChangesClientWinsAsync(DbContext, int maxRetries, CancellationToken)` — retries on conflict, re-applies client values over refreshed DB originals
- `SaveChangesDatabaseWinsAsync(DbContext, int maxRetries, CancellationToken)` — retries on conflict, reloads entity and discards client changes
- `IOptimisticEntity` interface with `byte[] RowVersion` property
- Diagnostic `RVRS001` for non-partial classes decorated with `[Optimistic]`
- Compatible with SQL Server (database-managed `rowversion` via `[Timestamp]` convention) and other providers (manual version management via `ValueGeneratedNever()` + interceptor)
