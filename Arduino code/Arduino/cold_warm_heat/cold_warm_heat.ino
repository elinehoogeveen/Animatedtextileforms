#include <Wire.h>
#include "Adafruit_MCP9808.h"
#include "Adafruit_TSL2561_U.h"

Adafruit_MCP9808 tempSensor = Adafruit_MCP9808();
Adafruit_TSL2561_Unified luxSensor = Adafruit_TSL2561_Unified(TSL2561_ADDR_FLOAT, 12345);

const int mosfetPin = 6;      // digital pin driving the MOSFET gate
const float tempMin = 15.0;   // °C
const float tempMax = 25.0;
const float luxThreshold = 1000.0; // adjust based on what counts as "bright" for your setup

void setup() {
  Serial.begin(9600);
  pinMode(mosfetPin, OUTPUT);

  tempSensor.begin();
  luxSensor.begin();
  luxSensor.enableAutoRange(true);
  luxSensor.setIntegrationTime(TSL2561_INTEGRATIONTIME_101MS);
}

void loop() {
  float tempC = tempSensor.readTempC();

  sensors_event_t event;
  luxSensor.getEvent(&event);
  float lux = event.light;

  bool isBright = lux > luxThreshold;
  bool tempInRange = (tempC >= tempMin && tempC <= tempMax);

  bool mosfetState = !(isBright && tempInRange); // false when bright+comfortable, true otherwise
  digitalWrite(mosfetPin, mosfetState ? HIGH : LOW);

  Serial.print("Temp: "); Serial.print(tempC);
  Serial.print(" C, Lux: "); Serial.print(lux);
  Serial.print(", MOSFET: "); Serial.println(mosfetState ? "ON" : "OFF");

  delay(1000);
}