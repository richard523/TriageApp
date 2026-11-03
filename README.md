# TriageApp

Small Blazor demo web app that combines:

1. **Automatic ticket triage** — type a feature request into a box; it is classified
   into the architectural layer it most likely lands in, a Jira ticket is created
   (via the Jira Cloud REST API v3), and you can approve or reject tickets.
2. **DB schema viewer** — see the live SQL Server schema of the app database.
3. **OOP mastery quiz** — Quizlet-style repetition grounded in a real textbook.

All configuration lives in a `.env` file at the repo root (see `.env.example`).
The app degrades gracefully: with no `.env` at all it runs in **demo mode**
(in-memory storage, local-only tickets, open approvals).

## Stack

| Piece | Technology |
|---|---|
| UI | Blazor (interactive server), .NET 8 |
| Auth | Microsoft / Entra ID (`MicrosoftAccount` cookie auth) |
| Database | SQL Server / SQL Server Express + EF Core 8 |
| Ticketing | Jira Cloud REST API v3 (Basic auth with API token) |

## Run it

```powershell
cd src\TriageApp
dotnet run
```

Then open https://localhost:5001 (the exact URL is printed by `dotnet run`).

## Environment variables

Copy `.env.example` to `.env` and fill in what you need:

| Variable | Purpose |
|---|---|
| `DB_CONNECTION_STRING` | SQL Server connection string (defaults to the local `SQLEXPRESS` instance) |
| `MICROSOFT_CLIENT_ID` / `MICROSOFT_CLIENT_SECRET` | Enables Microsoft sign-in + locks approvals to signed-in users |
| `JIRA_BASE_URL`, `JIRA_USER_EMAIL`, `JIRA_API_TOKEN`, `JIRA_PROJECT_KEY` | Enables real Jira ticket creation |
| `JIRA_ISSUE_TYPE` | Issue type to create (default `Task`) |
| `JIRA_APPROVE_TRANSITION`, `JIRA_REJECT_TRANSITION` | Optional Jira workflow transitions applied when you approve/reject |
| `QUIZ_MASTERY_THRESHOLD` | Consecutive correct answers required to master a question (default `4`) |

### Microsoft auth setup

1. Azure Portal → Entra ID → App registrations → New registration.
2. Add redirect URI `https://localhost:5001/signin-microsoft`.
3. Create a client secret and put its value in `MICROSOFT_CLIENT_SECRET`; the
   application (client) ID goes in `MICROSOFT_CLIENT_ID`.

### Jira setup

1. Create an API token: https://id.atlassian.com/manage-profile/security/api-tokens
2. `JIRA_USER_EMAIL` must be the Atlassian account email tied to that token.
3. The user needs *create issue* permission on `JIRA_PROJECT_KEY`.

## The OOP quiz

Questions are grounded in a real textbook:

> Matt A. Weisfeld, *The Object-Oriented Thought Process*, 5th Edition,
> Addison-Wesley, 2019 (ISBN 978-0135181966).

Each question carries a chapter citation (see `src/TriageApp/Data/quiz.json`).
The bank contains 500 questions generated from 60 core OOP concepts (encapsulation,
inheritance, composition, polymorphism, interfaces, coupling, cohesion, object
life cycle, frameworks, and more) across eight question styles: definition,
term-recognition, benefit, example, code-snippet, anti-practice, myth-busting,
and NOT-questions — plus curated textbook questions and code-output puzzles.

Mastery rules, per the spec: you must answer a question correctly
`QUIZ_MASTERY_THRESHOLD` times in a row to move on; a wrong answer resets the
streak and sends you back to the start of the loop; progress is saved per user
(SQL Server when reachable, in-memory otherwise) and can be reset with
**Restart progress**.

## Architecture classifier

`Services/ArchitectureClassifier.cs` maps request text to one of five layers —
Presentation/UI, Application/API, Domain/Business Logic, Data Access,
Infrastructure/Integration — with word-boundary keyword evidence and a
confidence score. The layer is shown on the triage result, stored with the
ticket, and attached to the Jira ticket as a label (`arch-…`) and in the
description.

## Credits

The OOP quiz is grounded in:

> **Matt A. Weisfeld, _The Object-Oriented Thought Process_ (Developer's Library), 5th Edition.**
> Addison-Wesley / Pearson, 2019 · ISBN 978-0-13-518196-6

- 📖 Buy the book (credit the author): [Amazon — The Object-Oriented Thought Process, 5th Edition](https://www.amazon.com/Object-Oriented-Thought-Process-Developers-Library/dp/0135181968)

All quiz questions were written for this demo app and cite the book's chapters for further study.
The book itself is © Pearson Education. If the quiz helps you, support the author by buying the book.
