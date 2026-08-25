# Garage Doctor

An analytics application answering one question: **what actually breaks in this specific car?**

Pick a make, model and year and you get a failure profile — which components fail, at what mileage, how it developed over time, and which recall campaigns cover that car. It is built on 2.2 million real defect complaints filed with the US National Highway Traffic Safety Administration.

It is not a shop and not a car catalogue. It is a tool for reading a large public dataset honestly.

## Data source and attribution

Data comes from the **NHTSA Office of Defects Investigation**, published as daily flat files at [static.nhtsa.gov/odi/ffdd](https://static.nhtsa.gov/odi/ffdd/) under `us-pd` — public domain, no usage conditions.

| File | Content | Rows measured |
|---|---|---|
| `FLAT_CMPL.txt` | consumer complaints since 1995 | 2 237 179 |
| `FLAT_RCL_POST_2010.txt` | recall campaigns from 2010 | 244 499 |
| `FLAT_RCL_PRE_2010.txt` | recall campaigns 1967–2009 | 81 715 |

**This repository contains no source data.** It ships the download script, the field specifications (`data/spec/`) and a manifest recording exactly which snapshot the project was built on. Reproduce the dataset with `./make.ps1 data`.

The reason is not licensing — the dataset is public domain and redistribution would be permitted. The reason is that `CDESCR`, the complaint narrative, is free text written by vehicle owners, who regularly type names, addresses and phone numbers into it. Republishing those records would mean publishing other people's personal data.

## Pipeline

```
download      data/raw/FLAT_CMPL.txt          1.5 GB, fetched once
parse         IAsyncEnumerable<string[]>      streaming, never loaded into memory
canonicalize  makes, models, components       pure logic, unit tested
ingest        MongoDB                         unordered bulk upserts, batches of 10 000
precompute    profiles collection             server-side aggregation pipelines
```

Each stage is separated so it can be tested on its own. The parser is a `StreamReader` loop yielding one record at a time: the source file is 1.5 GB and reading it into memory is not an option. The full ingest runs in well under the 500 MB budget.

Ingest is **idempotent**. Document `_id` is the NHTSA identifier (`CMPLID` for complaints, `RECORD_ID` for recalls) and writes go through `ReplaceOneModel` with `IsUpsert`, so re-running replaces instead of duplicating.

## Personal data is dropped at ingest

Eight source columns never reach the database: `DEALER_NAME`, `DEALER_TEL`, `DEALER_CITY`, `DEALER_STATE`, `DEALER_ZIP`, `VEHICLE_OPERATOR`, `VIN` and `CITY`. The parser can read them; the mapper does not map them, and the `Complaint` document type has no properties to hold them.

This is enforced by tests, not by convention: one test walks a serialized BSON document recursively and fails if any of those element names appear at any depth, another asserts the exact camelCase element set. Adding a personal-data field to the model breaks the build.

## Canonicalization

**Components.** The NHTSA taxonomy drifted over 30 years, so the same part appears under several category names — `ENGINE` alongside `ENGINE AND ENGINE COOLING`, `SERVICE BRAKES` alongside `SERVICE BRAKES, HYDRAULIC`. All 55 measured top-level categories are folded into **26 canonical groups** through `data/component-groups.json`, a hand-editable map. Anything unmapped falls into `OTHER` and is reported, so a future taxonomy change surfaces instead of hiding.

**Makes.** The raw file holds 1069 distinct `MAKETXT` values. Normalization plus `data/make-aliases.json` reduces that to **1023**, and that is where honest canonicalization ends. The tail is not noise: 740 makes have fewer than 10 complaints and together account for 0.10% of the data, but they are genuinely distinct trailer, bus, RV and motorcycle manufacturers — WABASH NATIONAL, VAN HOOL, LUFKIN, DORSEY. Merging them by string similarity would corrupt the taxonomy to make a number look better, so they stay.

One consequence worth stating: after merging `MERCEDES`, `MERCEDES BENZ` and `MERCEDES-BENZ`, the canonical make holds noticeably more complaints than any single raw spelling did.

## What the data does not say

These belong on screen next to every chart, not just in a readme.

- **US market only.** European engine variants largely do not appear. Renault, Peugeot and Opel are statistically absent.
- **Complaints are not failure rates.** They are filed voluntarily. A model sold in larger numbers collects more complaints regardless of quality. Nothing here is normalized by fleet size, because NHTSA does not publish it.
- **Mileage is present in about half the records** (1 112 861 of 2 165 161 vehicle complaints). Every histogram therefore carries its sample size — `WithMileage` is a field on the profile, not an afterthought.
- **Recall rows are not campaigns.** One row is a campaign × model × model-year combination, which is why MERCEDES-BENZ leads the recall file with 45 411 rows while having far fewer complaints. That is a fragmented model range, not a quality signal.

## Why these technologies

**MongoDB without EF Core.** A complaint is a natural document: a vehicle, a component hierarchy, a narrative and event flags. A text index over the narrative gives full-text search with no extra infrastructure, and one aggregation pipeline computes every failure profile in a single pass. Skipping the ORM is deliberate — it puts index design, aggregation and document mapping in plain sight.

**Precomputed profiles.** The `profiles` collection is intentional denormalization. The profile for `audi|a3|2015` is computed once during ingest rather than on every page view, which is the tradeoff between normalization and response time made explicit.

**Testcontainers.** Integration tests run against a real `mongo:8` container started for the test run, so index behaviour, aggregation results and BSON mapping are verified against the actual database rather than a mock.

## Running it

There is no .NET SDK on the host; everything runs in containers.

```powershell
./make.ps1 up        # mongo, redis, mongo-express
./make.ps1 data      # download and unpack the NHTSA files, refresh data/MANIFEST.md
./make.ps1 test      # unit and integration tests
./make.ps1 ingest    # full ingest, indexes and precomputed profiles
./make.ps1 mongo     # mongosh on the garagedoctor database
```

The ingest console accepts `--resume` (keep existing collections), `--limit=N` (stop after N documents, useful for a smoke run), `--skip-recalls` and `--data-dir=path`. It downloads and unpacks the source files itself when they are missing.

Mongo Express is on `http://127.0.0.1:8091`, the web application on `http://127.0.0.1:5080`.

## Repository layout

```
src/GarageDoctor.Domain           documents, streaming parser, mappers, canonicalizers
src/GarageDoctor.Infrastructure   Mongo context, indexes, ingest services, aggregations
src/GarageDoctor.Ingest           console worker running the pipeline end to end
src/GarageDoctor.Web              ASP.NET Core MVC application
tests/GarageDoctor.UnitTests      parser, mappers and canonicalizers on synthetic fixtures
tests/GarageDoctor.IntegrationTests  Testcontainers against mongo:8
tests/fixtures                    hand-written TSV fixtures, no real records
```

Unit tests never touch the real dataset. The fixtures are written by hand precisely so they cover the edge cases on purpose — `YEARTXT = 9999`, four-level component paths, absurd mileages, both line ending conventions — instead of hoping a random sample happens to contain them.
