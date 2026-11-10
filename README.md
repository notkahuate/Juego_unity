# El Cartógrafo del Tiempo — CartoGame

Demo jugable en Unity (3D, Built-in Render Pipeline). Viaja desde el Nexo del Tiempo a la Era Pirata y la Era Samurái, derrota a los jefes y recupera los cristales.

## Contenido

- `Assets/CartoGame/Scripts/` — scripts del juego (jugador, enemigos, armas, HUD, tienda, portales…)
- `Assets/CartoGame/Editor/CartoGameBuilder.cs` — constructor que genera las escenas
- `Assets/CartoGame/LEEME.txt` — instrucciones completas y controles
- `Packages-unity/CartoGame_Juego.unitypackage` — el paquete original para importar directamente

## Cómo instalar

1. Crea un proyecto de Unity con la plantilla 3D (Built-in Render Pipeline).
2. Importa tus assets: `Assets > Import Package > Custom Package... > carto-game.unitypackage`
3. Importa `Packages-unity/CartoGame_Juego.unitypackage` (o copia la carpeta `Assets/CartoGame` a tu proyecto).
4. Pulsa **"Construir ahora"** (o menú `CartoGame > Construir juego (crear escenas)`).
5. Se abre la escena del Nexo. Pulsa Play.

## Controles

| Tecla | Acción |
|---|---|
| WASD | Moverse |
| Ratón | Cámara (rueda = zoom) |
| Shift | Correr |
| Espacio | Esquivar |
| Clic izquierdo / derecho | Ataque cuerpo a cuerpo / a distancia |
| Q / R | Cambiar arma cuerpo a cuerpo / a distancia |
| E | Interactuar |
| 1, 2, 3 | Comprar mejoras en el altar |
| Esc | Pausa |
