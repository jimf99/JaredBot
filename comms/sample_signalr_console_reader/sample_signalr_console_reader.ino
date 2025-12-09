#include <WiFi.h>
#include <ESPAsyncWebServer.h>
#include <AsyncTCP.h>

// Replace with your Wi-Fi credentials
const char* ssid = "YOUR_SSID";
const char* password = "YOUR_PASSWORD";

AsyncWebServer server(80);
AsyncWebSocket ws("/ws"); // WebSocket endpoint

// Broadcast data to all connected WebSocket clients
void notifyClients(const String &message) {
  ws.textAll(message);
}

// Handle WebSocket events
void onWsEvent(AsyncWebSocket *server, AsyncWebSocketClient *client,
               AwsEventType type, void *arg, uint8_t *data, size_t len) {
  if (type == WS_EVT_CONNECT) {
    Serial.printf("Client #%u connected\n", client->id());
  } else if (type == WS_EVT_DISCONNECT) {
    Serial.printf("Client #%u disconnected\n", client->id());
  } else if (type == WS_EVT_DATA) {
    // Optional: handle messages from client
    String msg = "";
    for (size_t i = 0; i < len; i++) {
      msg += (char) data[i];
    }
    Serial.printf("Received from client: %s\n", msg.c_str());
  }
}

void setup() {
  Serial.begin(115200);

  WiFi.begin(ssid, password);
  Serial.print("Connecting to WiFi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nConnected!");
  Serial.println(WiFi.localIP());

  ws.onEvent(onWsEvent);
  server.addHandler(&ws);

  // Serve HTML page
  server.on("/", HTTP_GET, [](AsyncWebServerRequest *request) {
    request->send_P(200, "text/html", R"rawliteral(
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>ESP32 Serial Data</title>
</head>
<body>
  <h2>ESP32 Live Serial Data</h2>
  <pre id="log"></pre>
  <script>
    var gateway = `ws://${window.location.hostname}/ws`;
    var websocket = new WebSocket(gateway);
    websocket.onmessage = function(event) {
      document.getElementById('log').textContent += event.data + "\\n";
    };
  </script>
</body>
</html>
    )rawliteral");
  });

  server.begin();
}

void loop() {
  // Example: read from Serial and broadcast
  if (Serial.available()) {
    String line = Serial.readStringUntil('\n');
    line.trim();
    if (line.length() > 0) {
      notifyClients(line);
    }
  }
}
