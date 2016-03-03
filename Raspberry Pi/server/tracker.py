#!/usr/bin/python
import gps
import time

session = gps.gps("localhost", "2947")
session.stream(gps.WATCH_ENABLE | gps.WATCH_NEWSTYLE)

count = 20
while True:
        try:
            report = session.next()
            f = open('/home/pi/mjpg/www/gps_log.txt', 'w')

            if report['class'] == 'TPV':
                data = ("%s, %s, %s, %s" % (report.lat,report.lon,report.speed,report.alt))
                f.write(data)
                f.close()

                time.sleep(1)

        except KeyError:
		pass
        except KeyboardInterrupt:
                quit()
        except StopIteration:
                session = None
