---
project: "DocumentAIlyzer"
version: 1
status: draft
created: 2026-05-25
context_type: greenfield
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: low
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

## Vision & Problem Statement

Back-office operators at an insurance company must manually open every incoming document, decide what type it is (claim, invoice, policy application, quote, etc.), and hand-extract the relevant data before passing it to the appropriate downstream system or team. The mix is dominated by scanned paper and photographed forms — machine-unreadable until someone reads them. The result is slow throughput, missed files, misclassification errors, and data-extraction mistakes that propagate into other systems.

The insight: AI-based document understanding has crossed the reliability threshold that makes automated classification and structured data extraction practical at scale. A categorizer-orchestrator can read each incoming file, determine its type and the processing path it requires, and route it without human intervention — turning a human bottleneck into a supervised, auditable pipeline.

## User & Persona

**Primary persona: Claims processor (insurance company back-office)**
Role: Receives batches of mixed incoming documents daily — first notice of loss forms, medical reports, police reports, photos, invoices, settlement letters — and manually decides what each is, what data to pull from it, and which team or system it goes to next. Currently spends significant time on classification and transcription before any actual claims work begins.

### Secondary personas
- Accounting team operator — handles invoices, payment confirmations, quotes
- Underwriting / insurance team operator — handles policy applications, risk assessments

## Success Criteria

### Primary
A single FNOL / insurance claim document travels the full pipeline without human intervention until the review step: a file is picked up from the company's designated document storage service, the categorizer identifies it as a claim, the claims extraction agent pulls structured data, the operator sees the result in the application, edits if needed, approves, and the document and its data are forwarded to the claims department. End-to-end with no silent failures.

### Secondary
The application shows a confidence score alongside each classification decision so operators know when to scrutinize more carefully.

### Guardrails
- No document is forwarded to any department without explicit operator approval — automatic forwarding bypassing the human gate is a compliance failure (AI Act), not just a bug.
- No file is silently lost: if the pipeline fails at any stage, the operator is notified and the document is held for manual review.

## User Stories

### US-01: Operator reviews and approves a processed claim

- **Given** a claims processor is logged in and the system has finished processing an FNOL document ingested from the company's designated document storage service
- **When** she receives a notification and opens the document in her queue
- **Then** she sees the assigned document category, the confidence score for that classification, all extracted data fields, and the routing destination (which person or downstream system will receive it); she can edit any field if needed, and click Approve to forward the document and its data to the correct destination

#### Acceptance Criteria
- All extracted fields are editable before approval
- Routing destination is visible before the operator commits to approve
- Approving without editing is valid (one-click approval if everything is correct)
- No forwarding occurs without an explicit Approve action
- Every edit and approval is recorded in the audit log with timestamp and operator identity
- Audit log captures both the original AI-produced value and any operator-edited value separately

## Functional Requirements

### Document Ingestion
- FR-001: System can monitor and pick up incoming files from the company's designated document storage service. Priority: must-have
  > Socrates: Counter-argument considered: none. Automatic pickup is core to the value proposition — without it the operator still manually triggers processing. Stands as written.

- FR-002: System can determine whether OCR or ICR processing is required for a file based on its format. Priority: nice-to-have
  > Socrates: Counter-argument considered: low-quality scans may fail without dedicated OCR. Resolution: vision-capable AI models handle clean scans well enough for v1. OCR added when real failure cases appear. Stands as nice-to-have.

- FR-003: System can apply OCR/ICR to extract readable text from scanned documents. Priority: nice-to-have
  > Socrates: Same resolution as FR-002. AI-direct extraction sufficient for v1. Stands as nice-to-have.

### Categorization & Routing
- FR-004: Categorizer can classify each document by type (v1: claim vs. non-claim); non-claim documents are routed to a manual review queue with an explicit flag. Priority: must-have
  > Socrates: Counter-argument accepted: binary claim/non-claim with no handling for non-claim creates a silent pile-up. Resolution: FR-004 updated — non-claim documents must be explicitly routed to a manual review queue, not ignored or dropped.

- FR-005: Categorizer can route each document to the appropriate specialized extraction agent based on its classified type, via an extensible routing mechanism. Priority: must-have
  > Socrates: Counter-argument accepted: agent registry must be designed for extensibility upfront or v2 will require a rewrite. Resolution: kept, with constraint that routing abstraction is designed to accept new agent types without modifying the categorizer.

### Data Extraction
- FR-006: Claims extraction agent can extract structured data fields directly from an FNOL / claim document (digital or scanned). Priority: must-have
  > Socrates: Counter-argument accepted: FNOL forms vary by insurer and region — without a fixed field schema, extraction quality is unknowable and untestable. Resolution: canonical FNOL field schema must be defined before implementation begins. Added to Open Questions.

### Operator Review
- FR-007: Operator can view the categorization result, confidence score, all extracted data fields, and the routing destination (person or downstream system) for each processed document. Priority: must-have
  > Socrates: Counter-argument accepted: confidence score without a defined threshold is meaningless to an operator. Resolution: a threshold or traffic-light display rule must be defined. Added to Open Questions.

- FR-008: Operator can edit any extracted field before approving. Priority: must-have
  > Socrates: Counter-argument accepted: unrestricted editing erases the distinction between AI output and human correction. Resolution: kept, with constraint that the audit log records both the original AI-produced value and the operator-edited value separately — not just the final approved value.

- FR-009: Operator can approve a document, triggering forwarding of the document and extracted data to the correct destination. Priority: must-have
  > Socrates: Counter-argument accepted: if the operator edits the document category during review, the routing destination may need to update. Resolution: kept, but behavior when category is changed during review (auto-update destination vs. manual re-selection) must be defined. Added to Open Questions.

- FR-012: Operator can flag a document for supervisor review directly from the review screen, when the operator cannot handle the file. Priority: nice-to-have
  > Socrates: Counter-argument considered: flagged documents may sit unaddressed without a supervisor notification path. Resolution: kept; notification path is a process concern scoped for v2 if needed. Stands as written.

### Supervision
- FR-010: Supervisor can override a routing or classification decision made by the categorizer. Priority: must-have
  > Socrates: Escalation trigger is covered by the supervisor role definition and FR-012 (operator-initiated escalation). The override capability is the governance tool; FR-012 is the trigger path. No structural change needed. Stands as written.

### Auditability
- FR-011: System can record an immutable audit log entry for every processing step, classification decision, field edit (original AI value + edited value), and approval — with timestamp and actor identity. Priority: must-have
  > Socrates: Counter-argument considered: none. Auditability is non-negotiable. Retention window and immutability mechanism are implementation details. Stands as written.

## Non-Functional Requirements

- Processed documents are available for operator review within 1 hour of arrival at the company's designated document storage service, measured from file arrival to appearance in the operator queue.
- All document content and extracted PII is processed and stored within the EU (or applicable local jurisdiction). No document content leaves the company's regulatory boundary for processing by external services.
- Extracted PII and document content is retained only for the minimum period required by applicable insurance regulation; retention beyond that requires explicit compliance justification.
- The pipeline handles up to 500 documents per day without throughput degradation or queue backlog.
- No document is forwarded to any destination without explicit operator approval — automated forwarding that bypasses the human review gate is a compliance failure (AI Act).

## Business Logic

The system determines the business type of each incoming document by analyzing its content alongside file metadata (filename, source mailbox, sender), then routes it — sending the document and its extracted structured data to the appropriate downstream system and notifying the responsible team.

The classification decision consumes two inputs: the raw document content (text or image, read directly by the AI) and file metadata that provides a prior (e.g., documents arriving from the claims mailbox are likely claims). The output of the combined classification + routing decision is twofold: a system-level action (document + structured data delivered to a downstream integration endpoint) and a human-level action (responsible team or person notified). The operator encounters this decision as a fait accompli — they review what the system decided, edit if wrong, and approve or escalate. They never start with a blank form.

## Access Control

Entry: Company SSO login via the company's SSO identity provider. Operators authenticate with corporate credentials — no separate account creation.

Two roles:
- **Operator** — can view the processing queue, review categorization results and extracted data, edit fields, approve documents, and (optionally) flag for supervisor escalation.
- **Supervisor** — all Operator capabilities plus the ability to override a routing or classification decision made by the categorizer.

## Non-Goals

- **No custom AI model training or fine-tuning in v1.** Classification and extraction rely on existing AI APIs as-is. Custom model investment requires accuracy baselines that don't exist yet — this is a v2+ decision.
- **No multi-tenant / multi-company support.** v1 serves a single insurance company. Multi-tenant data isolation, tenant onboarding, and per-company configuration are out of scope.

## Open Questions

1. **What is the canonical FNOL field schema?** The claims extraction agent needs a fixed list of fields to extract (claimant name, date of loss, policy number, loss type, etc.). Without this the FR cannot be implemented or tested. Owner: user/domain expert. Block: yes — must be defined before FR-006 implementation begins.

2. **What confidence threshold triggers heightened operator scrutiny?** A raw score without a threshold rule is unactionable for operators. A traffic-light display rule (green/amber/red) or a numeric floor must be defined. Owner: user. Block: yes — needed before FR-007 display can be designed.

3. **When an operator edits the document category during review, does the routing destination update automatically or must the operator re-select it?** Affects FR-009 implementation and audit trail design. Owner: user. Block: no — can be deferred to implementation design, but should be resolved before FR-009 is built.
