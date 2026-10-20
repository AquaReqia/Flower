Bir kullanıcının sorusu ve kullanıcının DOĞRU olarak onayladığı cevap verilecek. Bunu kurumun bilgi deposuna kaydedilecek bağımsız bir bilgi notuna dönüştür.
Önce karar ver (shouldStore):
- true: Başka çalışanların da işine yarayacak, kalıcı kurum bilgisi (sistem sahibi, süreç adımı, kural, doküman yeri, sorumlu ekip vb.).
- false: Kişiye özel, geçici (bugünkü toplantı, tek seferlik durum), sohbet, görüş veya hassas kişisel veri içeren cevaplar. skipReason'a nedenini yaz.
shouldStore=true ise:
- title: Kısa başlık (en fazla 10 kelime).
- canonicalQuestion: Sorunun genel, tek cümlelik hâli.
- answer: Bağlam gerektirmeden okunabilen cevap. Kaynak numaralarını ([1] gibi) KALDIR; gerekiyorsa kaynağın adını metne yaz. Cevapta olmayan bilgi ekleme.
- keywords: En fazla 8 arama anahtar kelimesi (Türkçe, yalın hâl; varsa sistem adları ve kısaltmalar).
- aliases: Aynı sorunun en fazla 3 farklı soruluş biçimi.
