# DictionaryImporter – Architecture (Source of Truth)

## 1. Vision
The DictionaryImporter is a deterministic, repeatable ETL pipeline that ingests dictionary sources (starting with Project Gutenberg – Webster) and produces a normalized semantic graph consisting of Words, Senses, Concepts, and Relationships.

### Goals
1. Support multiple heterogeneous dictionary sources.
2. Preserve source fidelity while producing a canonical semantic model.
3. Enable graph-based querying (synonyms, see-also, belongs-to, etc.).
4. Ensure idempotent, restart-safe imports.

### Non-Goals
1. Linguistic correctness beyond the source material.
2. Runtime inference or AI-based enrichment.
3. UI or end-user presentation concerns.

---

## 2. High-Level Pipeline

1. Raw Import
2. Parsing
3. Canonicalization
4. Concept Creation
5. Graph Node Materialization
6. Graph Edge Materialization
7. Post-Processing & Verification

Each stage is append-only and must be independently re-runnable.

---

## 3. Core Domain Model (Glossary)

### DictionaryEntry (Raw)
Represents a raw, unprocessed record from a source file.
- Source-specific
- No semantic guarantees

### DictionaryEntryParsed (Sense)
Represents a single parsed sense.
- Atomic meaning unit
- One sense ≠ one word
- Immutable once created

### CanonicalWord
Normalized word form used for linking.
- Case-insensitive
- Language-aware (future)

### Concept
Semantic meaning abstraction.
- Exactly one Concept per DictionaryEntryParsed
- Concepts are never created from cross-references

### GraphNode
Materialized node in the graph.
- Backed by a domain entity (Word, Sense, Concept)
- NodeId is deterministic

### GraphEdge
Directed relationship between GraphNodes.
- Typed (BELONGS_TO, SEE, SYNONYM, etc.)
- No edge may imply concept creation

---

## 4. Authoritative Invariants (Critical)

1. One DictionaryEntryParsed → Exactly one Concept
2. Concepts are created only during Concept Creation stage
3. Cross-references (SEE, SEE_ALSO) never create Concepts
4. GraphEdge creation must not change cardinality of Concepts
5. Re-running any stage must not create duplicates
6. GraphNodeId and GraphEdge identity are deterministic

Violating any invariant is a bug.

---

## 5. Stage Responsibilities

### 5.1 Raw Import
1. Read source files
2. Persist verbatim content
3. No parsing, no linking

### 5.2 Parsing
1. Split raw entries into atomic senses
2. Extract definitions, parts of speech, references
3. Assign DictionaryEntryParsedId

### 5.3 Canonicalization
1. Normalize words
2. Resolve spelling variants
3. Create CanonicalWord records

### 5.4 Concept Creation
1. Create one Concept per DictionaryEntryParsed
2. No external references allowed
3. Deterministic ConceptId

### 5.5 Graph Node Materialization
1. Create nodes for Words, Senses, Concepts
2. No relationships

### 5.6 Graph Edge Materialization
1. Create BELONGS_TO edges (Sense → Concept)
2. Create SEE / SEE_ALSO edges (Sense → Sense)
3. Must not create nodes

### 5.7 Post-Processing & Verification
1. Validate invariants
2. Detect duplicates
3. Report metrics

---

## 6. ID Strategy

1. All IDs are deterministic
2. IDs are stable across re-imports
3. SourceCode is always part of the identity

---

## 7. Error Handling Philosophy

1. Fail fast on invariant violations
2. Partial success is allowed per stage
3. Errors are source-scoped

---

## 8. Open Questions

1. Multi-language support strategy
2. Polysemy across sources
3. Concept merging across dictionaries

---

## 9. How to Use This Document

1. This document is authoritative
2. All design discussions must reference a section here
3. Changes require an ADR

# DictionaryImporter – Architecture (Source of Truth)

## 1. Vision
The DictionaryImporter is a deterministic, repeatable ETL pipeline that ingests dictionary sources (starting with Project Gutenberg – Webster) and produces a normalized semantic graph consisting of Words, Senses, Concepts, and Relationships.

### Goals
1. Support multiple heterogeneous dictionary sources.
2. Preserve source fidelity while producing a canonical semantic model.
3. Enable graph-based querying (synonyms, see-also, belongs-to, etc.).
4. Ensure idempotent, restart-safe imports.

### Non-Goals
1. Linguistic correctness beyond the source material.
2. Runtime inference or AI-based enrichment.
3. UI or end-user presentation concerns.

---

## 2. High-Level Pipeline

1. Raw Import
2. Parsing
3. Canonicalization
4. Concept Creation
5. Graph Node Materialization
6. Graph Edge Materialization
7. Post-Processing & Verification

Each stage is append-only and must be independently re-runnable.

---

## 3. Core Domain Model (Glossary)

### DictionaryEntry (Raw)
Represents a raw, unprocessed record from a source file.
- Source-specific
- No semantic guarantees

### DictionaryEntryParsed (Sense)
Represents a single parsed sense.
- Atomic meaning unit
- One sense ≠ one word
- Immutable once created

### CanonicalWord
Normalized word form used for linking.
- Case-insensitive
- Language-aware (future)

### Concept
Semantic meaning abstraction.
- Exactly one Concept per DictionaryEntryParsed
- Concepts are never created from cross-references

### GraphNode
Materialized node in the graph.
- Backed by a domain entity (Word, Sense, Concept)
- NodeId is deterministic

### GraphEdge
Directed relationship between GraphNodes.
- Typed (BELONGS_TO, SEE, SYNONYM, etc.)
- No edge may imply concept creation

---

## 4. Authoritative Invariants (Critical)

1. One DictionaryEntryParsed → Exactly one Concept
2. Concepts are created only during Concept Creation stage
3. Cross-references (SEE, SEE_ALSO) never create Concepts
4. GraphEdge creation must not change cardinality of Concepts
5. Re-running any stage must not create duplicates
6. GraphNodeId and GraphEdge identity are deterministic

Violating any invariant is a bug.

---

## 5. Stage Responsibilities

### 5.1 Raw Import
1. Read source files
2. Persist verbatim content
3. No parsing, no linking

### 5.2 Parsing
1. Split raw entries into atomic senses
2. Extract definitions, parts of speech, references
3. Assign DictionaryEntryParsedId

### 5.3 Canonicalization
1. Normalize words
2. Resolve spelling variants
3. Create CanonicalWord records

### 5.4 Concept Creation
1. Create one Concept per DictionaryEntryParsed
2. No external references allowed
3. Deterministic ConceptId

### 5.5 Graph Node Materialization
1. Create nodes for Words, Senses, Concepts
2. No relationships

### 5.6 Graph Edge Materialization
1. Create BELONGS_TO edges (Sense → Concept)
2. Create SEE / SEE_ALSO edges (Sense → Sense)
3. Must not create nodes

### 5.7 Post-Processing & Verification
1. Validate invariants
2. Detect duplicates
3. Report metrics

---

## 6. ID Strategy

1. All IDs are deterministic
2. IDs are stable across re-imports
3. SourceCode is always part of the identity

---

## 7. Error Handling Philosophy

1. Fail fast on invariant violations
2. Partial success is allowed per stage
3. Errors are source-scoped

---

## 8. Open Questions

1. Multi-language support strategy
2. Polysemy across sources
3. Concept merging across dictionaries

---

## 9. How to Use This Document

1. This document is authoritative
2. All design discussions must reference a section here
3. Changes require an ADR

