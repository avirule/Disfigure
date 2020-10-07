## Packet Format

|           | Length | Alignment Constant | Initialization Vector | Packet Type | UTC Timestamp |  Content  |
|----------:|:------:|:------------------:|:---------------------:|:-----------:|:-------------:|:---------:|
| Encrypted |   No   |         No         |           No          |     Yes     |      Yes      |    Yes    |
|     Bytes |    4   |          4         |           16          |      1      |       8       | Undefined |
 