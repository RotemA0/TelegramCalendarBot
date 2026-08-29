# TelegramCalendarBot

A personal Telegram bot that:
1. **Daily briefing** — once a day, sends today's Google Calendar events to your Telegram chat.
2. **Natural-language event creator** — message the bot something like `Math test on Thursday at 10 AM` and it parses out a title + date/time and creates a calendar event.

Runs entirely on **GitHub Actions** — no server, no cloud hosting bill, no credit card required anywhere.

## Why GitHub Actions instead of a hosted server

Telegram bots normally receive messages via a webhook, which needs an always-on public HTTPS
server. Instead, this repo has:

- **`.github/workflows/telegram-poll.yml`** — runs every 5 minutes, asks Telegram
  (`getUpdates`) whether there are new messages, and processes any it finds. Stands in for the
  webhook.
- **`.github/workflows/daily-briefing.yml`** — runs every hour, but the app itself checks the
  real Asia/Jerusalem clock and only actually sends once, at 08:00 local time (a state file
  prevents double-sends; this also correctly handles Israel's daylight-saving shift, which a
  raw UTC cron expression can't).

Both workflows commit their small state files (`state/telegram-offset.txt`,
`state/last-briefing-date.txt`) back to the repo via the built-in `GITHUB_TOKEN` — that's how
state survives between runs, since each run is a fresh, stateless container.

This repo is **public** specifically so GitHub's Actions minutes are unconditionally free —
private repos only get 2,000 free minutes/month, which 5-minute polling would exceed. No
secrets live in the code; everything sensitive is a GitHub encrypted secret, injected as an
env var only for the duration of a run.

## Required GitHub secrets

Set these under **Settings → Secrets and variables → Actions**:

| Secret | What it is |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Your bot's token from [@BotFather](https://t.me/BotFather) |
| `TELEGRAM_CHAT_ID` | Your Telegram chat ID (only messages from this chat are processed) |
| `GOOGLE_CALENDAR_ID` | The calendar ID events get read from / written to |
| `GOOGLE_SERVICE_ACCOUNT_JSON_B64` | Base64-encoded content of your Google service-account key JSON. The calendar must be shared with that service account's `client_email`. |

## Running locally

```
dotnet user-secrets set "Telegram:BotToken" "..."
dotnet user-secrets set "Telegram:ChatId" "..."
dotnet user-secrets set "Google:CalendarId" "..."
dotnet user-secrets set "Google:ServiceAccountKeyPath" "service-account.json"

dotnet run -- poll              # check for and process new Telegram messages
dotnet run -- daily-briefing    # send today's events (only if it's currently 08:00 Israel time)
dotnet run -- daily-briefing --force   # send right now regardless of time/day-already-sent
```

`service-account.json` and `test.json` are git-ignored — never commit them.

## Testing on the live deployment

- **Event creation:** message the bot something like `Dentist at 3pm tomorrow`. Within 5
  minutes (the poll interval) you should get a confirmation reply and see the event on your
  calendar. To check sooner, go to the repo's **Actions** tab → **Telegram Poll** → **Run
  workflow**.
- **Daily briefing:** it fires automatically at 08:00 Asia/Jerusalem time. To trigger it on
  demand, go to **Actions** → **Daily Briefing** → **Run workflow** → tick **force**.
