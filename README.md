# Disfigure

Disfigure is a protocol-agnostic platform for creating decentralized instant messaging applications. 


## Project Goals

- Simplicity of function
    - The project should strive to remain focused implementing and refining all features listed herein, and avoid any unnecessary—or otherwise out-of-scope—features.
- Hosting Disfigure servers on personal machines
- Hosting Disfigure client-server on personal machines
    - Client-server acting as a pass-through and personal database.
        - For instance, you'd connect your client (on machine A) to a client-server you've hosted (on machine B). The client-server is then connected to Disfigure servers.
        - This allows simple record-keeping and message histories for personal converstions, without the requirement for a central Disfigure record server (and thus, removing Disfigure developers as a potential security risk).
- Connecting to Disfigure client-servers or servers from personal machines
- Voice communication between clients, client-servers, and clients-through-servers
- MMS capabilities, including support for embedded video, image, and sound files
- *Reasonably comprehensive* support for Unicode, to whatever extent Disfigure developers can manage.
- A simple-to-read, mono-spaced font and layout.
    - To give an idea of what this goal means, take irssi. In my eyes, it is a well-made application that embodies form over function, while still maintaining good-enough form. While I don't seek to emulate irssi's layout (it doesn't translate well to a desktop/non-terminal application), the overall design is some that inspires me.
    - **Personal aside on this point:**
        - I feel visual bloat is becoming a real issue in applications within the last several years. Applications like TeamSpeak were rather robust with their user interfaces; while it may not have been *pretty*, it was *very functional*. I believe that both of these goals—prettiness, and functionality—can be achieved in tandem.
        - Overall though, the goal of any design choice should be *functionality-first*. I'm certainly not aiming to create the most beautiful application. Firmly, I believe that an application that makes it easiest to do what users want to do (in this case, chat) will is far more useful than an application which is pretty, and a pain in the ass to use.
