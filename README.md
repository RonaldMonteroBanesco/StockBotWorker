from pathlib import Path

# Define the README content
readme_content = """# 📈 StockBotWorker

**StockBotWorker** is a background service built with .NET that listens for stock quote requests via RabbitMQ, fetches real-time quotes from the [Stooq API](https://stooq.com), and sends formatted responses back to a chat queue.  
It is designed to work alongside a chat application, responding automatically to `/stock=CODE` commands.

---

## 🚀 Features

- **RabbitMQ Integration** — Listens for stock requests on `stockQueue` and sends responses to `chatQueue`.
- **Real-time Stock Data** — Fetches quotes from Stooq in CSV format and parses results.
- **Error Handling** — Sends an error message to chat if a quote cannot be retrieved.
- **Survivability** — Reconnects automatically if RabbitMQ restarts.
- **Lightweight & Fast** — Runs as a .NET Worker Service.

---

## 📦 Requirements

- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) or later
- [RabbitMQ](https://www.rabbitmq.com/) running locally or remotely
- Internet access (to fetch data from [stooq.com](https://stooq.com))

---

## ⚙️ Environment Variables

You can configure RabbitMQ connection details with environment variables (optional):

| Variable          | Default   | Description                       |
|-------------------|-----------|-----------------------------------|
| `RABBITMQ_HOST`   | localhost | RabbitMQ hostname                 |
| `RABBITMQ_USER`   | guest     | RabbitMQ username                 |
| `RABBITMQ_PASS`   | guest     | RabbitMQ password                 |

---

## 🔧 Installation & Running

1. **Clone the repository** (if applicable)  
   ```bash
   git clone https://github.com/your-username/StockBotWorker.git
   cd StockBotWorker
   ```

2. **Restore dependencies**  
   ```bash
   dotnet restore
   ```

3. **Run the worker**  
   ```bash
   dotnet run
   ```

4. **Test it**  
   - Publish a message to the `stockQueue` queue in RabbitMQ with the body:  
     ```
     aapl.us
     ```
   - The worker will fetch the latest Apple stock price and send it to `chatQueue`.

---

## 📜 Example Flow

1. User types in chat:  
   ```
   /stock=aapl.us
   ```
2. Chat app sends `"aapl.us"` to RabbitMQ queue `stockQueue`.
3. **StockBotWorker**:
   - Fetches CSV from Stooq.
   - Extracts the closing price.
   - Publishes a formatted reply (e.g., `"AAPL.US quote is $191.50 per share"`) to `chatQueue`.
4. Chat app displays the bot's message.

---

## 🛠 Project Structure

```
StockBotWorker/
│
├── Worker.cs             # Main background service logic
├── Program.cs            # Host builder & DI setup
├── appsettings.json      # Configuration file
└── StockBotWorker.csproj # Project file
```

---

## 🐛 Error Handling

If a stock code is invalid or the Stooq API is unreachable, the bot will:
- Log the error to console.
- Publish a message like:
  ```
  bot error: could not fetch quote for <CODE>
  ```

---