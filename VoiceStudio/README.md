# Voice Studio

Ta voix + tout ce que tu écoutes sur le PC, mélangés et envoyés dans un micro virtuel que Discord utilise comme entrée.

```
Microphone ──────────────────────────┐
                                     ├─► Mixeur ─► Limiteur ─► « CABLE Input » ═► « CABLE Output » ─► Discord
Applications du PC (sauf Discord) ───┘     (VB-CABLE, le câble virtuel)
```

## Ce que fait l'application

Cinq pages, en onglets en haut de la fenêtre.

**Mixer** — une table de mixage à tranches verticales : volume général, micro, soundboard, puis quatre bus — **Jeu, Chat, Média, Autres**. Chaque application qui joue vraiment du son apparaît sous forme d'étiquette dans un bus et se **fait glisser d'un bus à l'autre** à la souris ; le volume se règle au fader du bus. Une application qui garde un flux ouvert sans rien jouer (Steam, les utilitaires de carte son) n'apparaît pas ; une application qui se tait disparaît au bout de 30 secondes. Le classement de départ se fait sur le nom (Spotify et les navigateurs vont dans Média) ; ton rangement est retenu d'une session à l'autre. Une colonne « Hors du mix » montre les applications qui jouent du son mais restent exclues, Discord en tête.

**Voix** — effets vocaux appliqués à ta voix : hauteur (grave/aiguë sans changer la vitesse), filtre, robot, distorsion, radio, téléphone, écho, réverbération. Combinables, avec 10 presets fournis et tes propres presets enregistrables.

**Micro** — le volume et le nettoyage du son :
- niveau d'entrée Windows et gain logiciel de -12 à +24 dB, si on t'entend trop bas sur Discord ;
- avertissement si le micro choisi est un périphérique virtuel (SteelSeries Sonar, VoiceMeeter…), cause fréquente de craquements ;
- bascule micro / source : une touche coupe le micro et envoie à sa place un bus ou le soundboard ;
- suppression du bruit de fond (souffle, ventilateur, bourdonnement), à utiliser à la place de Krisp — **désactivée par défaut** : sans elle ni effet vocal, la voix part vers Discord exactement telle qu'elle est captée ;
- noise gate, qui ferme le micro entre deux phrases ;
- push-to-talk, avec une touche qui marche même hors de la fenêtre ;
- baisse automatique de tous les bus quand tu parles ;
- retour audio pour s'entendre dans son casque.

**Soundboard** — bibliothèque d'extraits joués dans le micro virtuel : WAV, MP3 et les formats que Windows sait lire, volume par son, raccourci clavier par son, plusieurs sons à la fois.

**Réglages** — latence, casque de retour, applications exclues, aide pour Discord.

En cas de craquements, la page Micro affiche un compteur de coupures audio : s'il monte pendant que tu parles, augmente la latence de sortie dans Réglages.

Partout : Discord est exclu de la capture (liste modifiable), sinon les voix de tes amis leur reviendraient en écho. Le volume général est protégé par un limiteur. Les réglages sont enregistrés dans `%AppData%\VoiceStudio\settings.json`.

### Reste à faire

Overlay flottant, barre système, démarrage automatique, profils complets (Discord / Gaming / Musique…), thème clair.

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
| Cœur du moteur (mixage, bus, bascule, limiteur, gain, effets, suppression de bruit, noise gate, ducking, push-to-talk, soundboard) | 82 tests automatiques, tous passent |
| ViewModels et réglages | Compilés sans erreur |
| Fenêtre WPF (XAML, `App.xaml.cs`, `MainWindow.xaml.cs`) | **Non compilée** : WPF ne se compile que sous Windows |
| Capture réelle (micro, applications), sortie VB-CABLE, Discord | **Non testée** : demande un PC Windows avec du son |

La V1 n'est donc validée que lorsque le test sur ton PC passe : parler et faire entendre la musique sur Discord.

## Architecture

```
src/VoiceStudio.Audio/          Moteur audio — aucune dépendance à l'interface
  AudioEngine.cs                Point d'entrée : démarre / arrête tout le flux
  Capture/                      ProcessLoopbackCapture (son d'une application), chaîne de capture
  Effects/                      Effets vocaux, suppression de bruit, noise gate, presets
  Mixing/                       MixerChannel, MixerEngine, AudioBus, AppAudioSource, SoundboardMixer
  Processing/                   Conversions de format, passage en stéréo, limiteur, auto-ducking
  Routing/                      Tampon circulaire sans verrou entre capture et sortie
  Services/                     AudioDeviceService, AudioCaptureService (micro),
                                SystemAudioCaptureService (sons du PC),
                                AudioMixerService, VirtualMicrophoneService,
                                VoiceEffectsService, SoundboardService, MonitorService
src/VoiceStudio/                Application WPF (MVVM)
  Views/                        Les cinq pages (Mixer, Voix, Micro, Soundboard, Réglages)
  ViewModels/  Settings/  Helpers/  Resources/Theme.xaml  MainWindow.xaml
tests/VoiceStudio.SelfTest/     Tests du moteur
```

Choix techniques :

- Format interne unique : 48 kHz, stéréo, float 32 bits. Chaque source est convertie à l'entrée.
- Threads de capture et thread de sortie reliés par un tampon circulaire sans verrou ; aucune allocation dans les callbacks audio.
- Les horloges du micro et du câble virtuel ne sont pas synchronisées : si un tampon dépasse 150 ms, le retard est rattrapé pour que la latence ne s'accumule pas. Quand les données manquent, le son descend et remonte en 2 ms au lieu de couper net : un à-coup s'entend comme un blanc, pas comme un craquement.
- La recherche des applications qui jouent du son tourne pendant l'audio : le travail coûteux (photo de tous les processus, lecture de leurs noms) n'est refait que lorsque la liste change.
- NAudio 2.2.1 (version stable). La version 3 intègre la capture par application mais est encore en préversion.
- La suppression de bruit travaille par fenêtres de 1024 échantillons qui se recouvrent : elle mesure en continu le niveau du bruit permanent dans chaque bande de fréquences et n'atténue que celles où la voix ne ressort pas. Elle ajoute environ 21 ms de latence quand elle est active.
