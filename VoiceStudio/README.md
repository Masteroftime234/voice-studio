# Voice Studio

Ta voix + tout ce que tu écoutes sur le PC, mélangés et envoyés dans un micro virtuel que Discord utilise comme entrée.

```
Microphone ──────────────────────────┐
                                     ├─► Mixeur ─► Limiteur ─► « CABLE Input » ═► « CABLE Output » ─► Discord
Applications du PC (sauf Discord) ───┘     (VB-CABLE, le câble virtuel)
```

## Ce que fait la V1

- Détection des micros et des sorties, mise à jour quand on branche ou débranche un périphérique.
- Capture du microphone (WASAPI, mode partagé).
- Capture de **tout ce que tu écoutes** : chaque application qui joue du son (Spotify, navigateur, jeux, vidéos…) est capturée séparément puis additionnée. Pas d'API Spotify, pas besoin de Premium.
- **Discord est exclu** de cette capture (liste modifiable) : sinon les voix de tes amis leur seraient renvoyées en écho.
- Volume, bouton couper et indicateur de niveau pour chaque source ; volume général avec limiteur anti-saturation.
- Sortie vers VB-CABLE, avec indicateur « le flux arrive / flux bloqué ».
- Réglages enregistrés dans `%AppData%\VoiceStudio\settings.json`.

### Déjà dans le moteur audio, pas encore dans la fenêtre

Ces fonctions sont écrites et testées côté moteur (`VoiceStudio.Audio`), mais la fenêtre n'a pas encore les écrans pour les piloter :

- Effets vocaux : voix grave / aiguë (sans changer la vitesse), robot, radio, téléphone, écho, réverbération, distorsion, filtre ; combinables ; 10 presets fournis ; presets personnalisés enregistrables.
- Noise gate, auto-ducking (sensibilité, volume minimum, vitesses), push-to-talk.
- Soundboard : décodage WAV / MP3, plusieurs sons en même temps (limite réglable), volume par son, arrêt.
- Retour audio de sa propre voix (désactivé par défaut).

### Reste à faire (interface)

Écrans Voice FX, Soundboard et Settings, overlay flottant, raccourcis clavier, barre système, profils, démarrage automatique.

## Installation

Sur le PC, deux choses seulement :

1. **VoiceStudio-Setup.exe** — l'installateur. Il contient déjà .NET : aucun SDK à installer.
2. **[VB-CABLE](https://vb-audio.com/Cable/)** — le câble virtuel (un pilote Windows), puis redémarrer le PC. Sans lui, Windows n'a aucun « micro virtuel » à proposer à Discord.

Windows 11 recommandé (voir « Limites »).

### Fabriquer l'installateur (sans rien installer sur son PC)

WPF ne se compile que sous Windows. Le dépôt contient un script GitHub Actions qui le fait gratuitement sur un PC Windows de GitHub :

1. Créer un dépôt sur github.com et y envoyer le contenu de ce dossier (branche `main`).
2. Onglet **Actions** → « Installateur Windows » → attendre la fin (quelques minutes).
3. Ouvrir l'exécution → **Artifacts** → télécharger `VoiceStudio-Setup`.

### Pour développer

Avec le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) ou Visual Studio 2022 :

```
dotnet run --project tests/VoiceStudio.SelfTest     (tests du moteur, sans périphérique)
dotnet run --project src/VoiceStudio                (l'application)
```

## Utilisation

1. Dans Voice Studio : choisir ton micro, choisir **CABLE Input** comme sortie virtuelle, cliquer **Démarrer**.
2. Dans Discord → Paramètres → Voix et vidéo :
   - Périphérique d'entrée : **CABLE Output (VB-Audio Virtual Cable)**.
   - Désactiver la suppression de bruit (Krisp), l'annulation d'écho et le contrôle automatique du gain : ils sont faits pour la voix et hachent la musique.
   - Régler la sensibilité de la voix à la main, assez bas.
3. Lancer de la musique : la ligne « Sons du PC » indique les applications capturées.

Pour que le micro s'appelle « Voice Studio Virtual Mic » dans Discord : Paramètres Windows → Son → Entrée → CABLE Output → Renommer.

## Limites à connaître

- **Capture par application** : c'est l'API Windows « Application Loopback ». Microsoft la documente à partir du build 20348, ce qui correspond en pratique à Windows 11. Sur Windows 10, elle peut ne pas fonctionner ; l'application l'indique alors dans la ligne d'état.
- Une application qui commence à jouer du son est détectée en 1 seconde environ. 8 applications au maximum en même temps.
- Les sons système de Windows (notifications) ne sont pas capturés.
- Le micro virtuel est celui de VB-CABLE. Créer un vrai périphérique « Voice Studio Virtual Mic » demanderait d'écrire et de signer un pilote Windows : hors de portée d'un prototype.
- Diffuser de la musique sur un salon peut poser des questions de droits selon l'usage.

## État des vérifications

| Partie | Vérifié |
|---|---|
| Moteur audio (`VoiceStudio.Audio`) | Compilé sans erreur contre le code source de NAudio 2.2.1 |
| Cœur du moteur (mixage, limiteur, effets, noise gate, ducking, push-to-talk, soundboard) | 63 tests automatiques, tous passent |
| ViewModels et réglages | Compilés sans erreur |
| Fenêtre WPF (XAML, `App.xaml.cs`, `MainWindow.xaml.cs`) | **Non compilée** : WPF ne se compile que sous Windows |
| Capture réelle (micro, applications), sortie VB-CABLE, Discord | **Non testée** : demande un PC Windows avec du son |

La V1 n'est donc validée que lorsque le test sur ton PC passe : parler et faire entendre la musique sur Discord.

## Architecture

```
src/VoiceStudio.Audio/          Moteur audio — aucune dépendance à l'interface
  AudioEngine.cs                Point d'entrée : démarre / arrête tout le flux
  Capture/                      ProcessLoopbackCapture (son d'une application), chaîne de capture
  Effects/                      Effets vocaux, noise gate, presets
  Mixing/                       MixerChannel, MixerEngine, SoundboardMixer, MultiSourceSampleProvider
  Processing/                   Conversions de format, passage en stéréo, limiteur, auto-ducking
  Routing/                      Tampon circulaire sans verrou entre capture et sortie
  Services/                     AudioDeviceService, AudioCaptureService (micro),
                                SystemAudioCaptureService (sons du PC),
                                AudioMixerService, VirtualMicrophoneService,
                                VoiceEffectsService, SoundboardService, MonitorService
src/VoiceStudio/                Application WPF (MVVM)
  ViewModels/  Settings/  Helpers/  Resources/Theme.xaml  MainWindow.xaml
tests/VoiceStudio.SelfTest/     Tests du moteur
```

Choix techniques :

- Format interne unique : 48 kHz, stéréo, float 32 bits. Chaque source est convertie à l'entrée.
- Threads de capture et thread de sortie reliés par un tampon circulaire sans verrou ; aucune allocation dans les callbacks audio.
- Les horloges du micro et du câble virtuel ne sont pas synchronisées : si un tampon dépasse 150 ms, le retard est rattrapé pour que la latence ne s'accumule pas.
- NAudio 2.2.1 (version stable). La version 3 intègre la capture par application mais est encore en préversion.
