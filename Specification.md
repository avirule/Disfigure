##Packet Format

| Timestamp (Ticks) | Type (Byte Enum) | Content Length (Integer) | Remaining |
|-------------------|------------------|--------------------------|-----------|
|      8 bytes      |     1 byte       |         4 bytes          | Undefined |
|       Total:      |    29 bytes      |                          |           |

##Server-Client Identity Exchange
###Server
  1. Identity (name, description, etc)
  2. channel list
  3. Connected users
 