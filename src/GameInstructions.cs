using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Cloud90.GameLink
{
    internal sealed class GameInstructions : Form
    {
        readonly List<Label> paragraphs = new List<Label>();

        public GameInstructions(string gameId, string gameTitle)
        {
            Text = "Instrucciones · " + gameTitle;
            ClientSize = new Size(760, 720);
            MinimumSize = new Size(640, 540);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Design.Background; ForeColor = Design.Text;
            Font = new Font("Segoe UI", 10);
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;

            var header = new GameBanner(gameId) { Size = new Size(760, 130), Dock = DockStyle.Top, Height = 130 };
            var title = Design.Label(gameTitle, 23, Design.Text);
            title.Font = new Font("Segoe UI Semibold", 23);
            title.SetBounds(24, 25, 690, 44); title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var subtitle = Design.Label("Prepara el juego y conéctalo con tu live", 11, Design.Muted);
            subtitle.SetBounds(26, 79, 650, 30); subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.AddRange(new Control[] { title, subtitle });

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(24, 16, 24, 16), ColumnCount = 3, RowCount = 1, BackColor = Design.Surface };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            var openCloud90 = new Button(); Design.Button(openCloud90, "Configurar en Cloud90", true); openCloud90.Dock = DockStyle.Fill; openCloud90.Margin = Padding.Empty;
            openCloud90.Click += delegate {
                try { Process.Start(new ProcessStartInfo("https://cloud90.dev/juegos/" + gameId) { UseShellExecute = true }); }
                catch (Exception error) { MessageBox.Show(this, "No se pudo abrir Cloud90: " + error.Message, Text); }
            };
            var close = new Button(); Design.Button(close, "Entendido", false); close.Dock = DockStyle.Fill; close.Margin = Padding.Empty; close.DialogResult = DialogResult.OK;
            footer.Controls.Add(openCloud90, 0, 0); footer.Controls.Add(close, 2, 0); CancelButton = close; AcceptButton = close;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Design.Background, AccessibleName = "Instrucciones de " + gameTitle };
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 0, Padding = new Padding(26, 22, 26, 14) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddSection(content, "1. Prepara tu instalación", Requirements(gameId));
            AddSection(content, "2. Conecta tu cuenta", "Entra a cloud90.dev › Juegos › " + gameTitle + ". Necesitas tener acceso a ese juego en tu cuenta. Pulsa Generar token, cópialo y vuelve a este juego en Game Link para pulsar Pegar token. Cada juego tiene su propio token; no lo compartas.");
            AddSection(content, "3. Abre la partida desde Game Link", Launch(gameId));
            AddSection(content, "4. Configura lo que hace tu chat", "En la página de este juego en Cloud90, configura las reglas: elige qué regalo, taps, follow o share activa cada efecto. Guarda los cambios y activa las reglas que quieras usar. Para los eventos del público, mantén conectada tu plataforma de transmisión en Cloud90 y deja Game Link abierto.");
            AddSection(content, "Cuándo funcionan los efectos", Gameplay(gameId));
            if (gameId == "gtasa") {
                AddSection(content, "Sabotajes para el chat", "Activa Modo troll en la web y guarda tus reglas. A pie: CJ borracho (hasta 15 s), controles invertidos (hasta 10 s), sin disparar (hasta 8 s), manos vacías y sin balas. Conduciendo un automóvil: volante invertido (hasta 10 s), acelerador pegado (hasta 3 s), llantas ponchadas y pista de hielo (hasta 8 s). Ruleta troll solo selecciona sabotajes compatibles y disponibles. CJ borracho y controles invertidos se sustituyen entre sí.");
                AddSection(content, "Impulsos y duración", "CJ sale volando y Trompo pueden provocar caídas o choques mortales: requieren desactivar Modo sin morir y activar Permitir impulsos peligrosos en la web. Cada uno espera al menos 60 segundos entre usos. Los efectos temporales terminan al pausar, desconectar, morir o cambiar de situación; no se trasladan a otro automóvil. Quitar un arma o balas y pinchar llantas son acciones instantáneas: no se deshacen al pausar. Reparar automóvil arregla las llantas. Los impulsos ya aplicados continúan con la física normal del juego.");
                AddSection(content, "Más sabotajes temporales", "Motor apagado impide acelerar o dar marcha atrás hasta 5 segundos; conserva los frenos y sustituye al acelerador pegado. Sin saltar dura hasta 10 segundos y Pies pegados inmoviliza a CJ a pie hasta 3 segundos, permitiendo apuntar y disparar. Niebla espesa dura hasta 15 segundos y Terremoto de cámara hasta 6 segundos. Se configuran como efectos de tus reglas en Cloud90.");
                AddSection(content, "Enemigos, perseguidores y obstáculos", "La web permite elegir de 1 a 3 enemigos (por defecto: 2), un vehículo perseguidor y tres conos o barreras. Las apariciones requieren estar en exteriores, fuera de misiones, a pie o conduciendo un automóvil. Si faltan carretera o espacio libre en el suelo, se rechazan. Pueden tardar hasta 5 segundos en cargar sus modelos. Duran hasta 45 segundos los enemigos, 60 el perseguidor y 30 los obstáculos. Hay límites de cantidad y un máximo total de ocho entidades de Cloud90. Al pausar, desconectar, morir o entrar en una misión se limpian; si el juego está en pausa, la limpieza se completa cuando vuelve a procesar la escena. Si CJ toma el vehículo perseguidor, se libera al juego en vez de borrarlo con CJ dentro. Modo sin morir no impide que estos enemigos o accidentes maten a CJ.");
            }
            AddSection(content, "Cómo pausar y resolver problemas", Troubleshooting(gameId));
            scroll.Controls.Add(content);
            scroll.ClientSizeChanged += delegate {
                int width = Math.Max(280, scroll.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
                content.SuspendLayout();
                foreach (Label paragraph in paragraphs) paragraph.MaximumSize = new Size(width, 0);
                content.ResumeLayout(true);
            };
            Controls.Add(scroll); Controls.Add(footer); Controls.Add(header);
        }

        void AddSection(TableLayoutPanel parent, string title, string text)
        {
            Label heading = Design.Label(title, 11, Design.Text); heading.Font = new Font("Segoe UI Semibold", 11);
            Label body = Design.Label(text, 10, Design.Muted);
            foreach (Label label in new[] { heading, body })
            {
                label.AutoSize = true; label.AutoEllipsis = false; label.MaximumSize = new Size(680, 0);
                label.Margin = new Padding(0, label == heading ? 4 : 0, 0, label == heading ? 8 : 22);
                label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                paragraphs.Add(label);
                int row = parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(label, 0, row);
            }
        }

        static string Requirements(string game)
        {
            switch (game)
            {
                case "hades": return "Necesitas Hades de Steam para Windows. Cierra el juego y pulsa Elegir carpeta: selecciona la carpeta de Hades que contiene Content y x64. Game Link instala la integración automáticamente. La opción Usar Vulkan permite abrir la versión Vulkan.";
                case "kof2002": return "Necesitas MAME para Windows y tus archivos kof2002.zip y neogeo.zip dentro de la carpeta roms de MAME. No descomprimas los ZIP. Pulsa Elegir carpeta y selecciona la carpeta que contiene mame.exe, no la carpeta roms. Game Link instala su plugin; no incluye el juego ni el BIOS.";
                case "mslug3": return "Necesitas MAME para Windows y tus archivos mslug3.zip y neogeo.zip dentro de la carpeta roms de MAME. No descomprimas los ZIP. Pulsa Elegir carpeta y selecciona la carpeta que contiene mame.exe, no la carpeta roms. Game Link instala su plugin; no incluye el juego ni el BIOS.";
                case "ctrnf": return "Necesitas el portable compatible de OCTR v0.6.4 y el módulo CTR Cloud90 instalado por separado, con tu imagen CUSA13795 1.21 compatible. Instala o actualiza el módulo con CTR y su cliente cerrados. En Elegir carpeta selecciona el portable que contiene run.py, bin y python; no selecciones la carpeta de la imagen del juego. Los efectos nuevos requieren el módulo con bridge 2.";
                case "cuphead": return "Necesitas Cuphead de Steam para Windows de 64 bits, build compatible 20241530. El DLC The Delicious Last Course es opcional. Cierra Cuphead y selecciona su carpeta, la que contiene Cuphead.exe y Cuphead_Data. Game Link instala el plugin automáticamente. Si indica que la versión no es compatible, no reemplaces archivos del juego.";
                case "gtasa": return "Necesitas GTA San Andreas clásico para Windows, con el ejecutable 1.0 US compatible con esta integración. Cierra GTA y selecciona la carpeta que contiene gta_sa.exe; en tu instalación es D:\\Grand Theft Auto San Andreas. Usa una instalación de un jugador sin SA-MP ni MoonLoader. Game Link instala el módulo automáticamente. Esta integración no es para la Definitive Edition.";
                default: return "Cierra el juego y selecciona su carpeta mediante Elegir carpeta.";
            }
        }
        static string Launch(string game)
        {
            switch (game)
            {
                case "ctrnf": return "Pulsa Abrir juego. Se abrirá el cliente original de CTR: allí pulsa Jugar. Los ajustes de idioma, pantalla, audio y mando se cambian en ese cliente. Inicia una carrera local de Arcade normal, con un jugador humano contra la CPU.";
                case "kof2002": return "Pulsa Abrir juego para iniciar KOF 2002 en MAME con el plugin activo. Inserta crédito e inicia una partida como jugador 1 usando tus controles de MAME. Juega contra la computadora; Fightcade y las partidas en línea no son compatibles.";
                case "mslug3": return "Pulsa Abrir juego para iniciar Metal Slug 3 en MAME con el plugin activo. Inserta crédito e inicia una partida de un jugador usando tus controles de MAME. Los efectos se aplican al jugador 1.";
                case "hades": return "Pulsa Abrir juego y comienza o continúa una huida. Mantén Game Link abierto: necesita leer las respuestas de Hades. Si el juego ya estaba abierto, ciérralo y vuelve a iniciarlo desde Game Link.";
                case "cuphead": return "Mantén Steam abierto con tu cuenta y pulsa Abrir juego en Game Link. Entra en una partida individual y comienza un nivel compatible. La misma conexión sirve para el juego base y el DLC.";
                default: return "Pulsa Abrir juego y comienza una partida nueva o carga una partida guardada. Espera hasta poder controlar a CJ. Mantén Game Link abierto mientras juegas.";
            }
        }
        static string Gameplay(string game)
        {
            switch (game)
            {
                case "hades": return "Los efectos requieren que Zagreus esté vivo en una cámara del inframundo, sin un menú abierto. Algunos poderes o enemigos dependen de la zona. Los efectos temporales terminan al cambiar de cámara.";
                case "kof2002": return "Los efectos funcionan durante una ronda activa contra la CPU, no en la demostración del título ni en las pantallas de selección. Los efectos para el rival ayudan a la computadora.";
                case "mslug3": return "Los efectos funcionan mientras juegas una partida que tú hayas iniciado, no en la demostración. Para crear enemigos debe haber enemigos compatibles en pantalla; si no hay, el juego puede rechazar el efecto.";
                case "ctrnf": return "Los efectos requieren una carrera local compatible en curso. Los disponibles dependen de tu módulo CTR Cloud90. Las acciones destructivas necesitan que las habilites expresamente en Cloud90 y que Modo sin morir esté desactivado.";
                case "cuphead": return "Los efectos se suspenden en menús, pausa, muerte, transiciones y cooperativo. Tampoco funcionan en los desafíos del Rey, el cementerio ni las zonas de selección de jefes. Los efectos exclusivos de suelo no funcionan en niveles de avión. Los efectos destructivos requieren autorización en Cloud90 y Modo sin morir desactivado.";
                default: return "Los efectos requieren a CJ vivo, en una partida individual activa, sin pausa ni cinemática. Puedes curarlo, darle armadura, sumar $1,000, quitar o cambiar su nivel de búsqueda y reparar su automóvil. Para reparar debes estar dentro de un automóvil que no haya sido destruido; no funciona a pie ni en moto.";
            }
        }
        static string Troubleshooting(string game)
        {
            string panic = game == "gtasa" || game == "cuphead" || game == "ctrnf" ? "También puedes usar F9 dentro del juego para pausar o reanudar los efectos. " : "";
            return "Pulsa Pausar efectos en Game Link para detenerlos y Reanudar efectos para volver a recibirlos. " + panic + "Si no conecta, revisa tu acceso y el token de este juego en cloud90.dev. Si un efecto no se aplica, comprueba que la partida esté activa, que las reglas estén habilitadas y que no haya una pausa o un enfriamiento. Abre Mostrar registro para consultar el motivo. Cierra el juego antes de instalar, actualizar o quitar su integración.";
        }
    }
}
