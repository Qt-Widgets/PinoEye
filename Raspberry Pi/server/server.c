#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <pthread.h>
#include <errno.h>
#include <wiringPi.h>
#include <wiringSerial.h>

#define PORT 8000

void *thread_main(void *);

char info[128];
int c_socket;

int main() {
 int s_socket;
 struct sockaddr_in s_addr, c_addr;
 int len;
 int n;
 int fd;
 int buff;
 int t_status;
 char *ptr;

 pthread_t thread;
 
 char rcvBuffer[30];
/*
 if ((fd = serialOpen ("/dev/ttyAMA0", 115200)) < 0) {
	 fprintf (stderr, "Unable to open serial device: %s\n", strerror (errno)) ;                   
	 return 1 ;
 }

 if (wiringPiSetup () == -1) {
	 fprintf (stdout, "Unable to start wiringPi: %s\n", strerror (errno)) ;
	 return 1 ;
 }
*/

 s_socket = socket(PF_INET, SOCK_STREAM, 0);

 memset(&s_addr, 0, sizeof(s_addr));
 s_addr.sin_addr.s_addr = htonl(INADDR_ANY);
 s_addr.sin_family = AF_INET;
 s_addr.sin_port = htons(PORT);

 if(bind(s_socket, (struct sockaddr*) &s_addr, sizeof(s_addr)) == -1) {
	 printf("Can not Bind\n");
  	 return -1;
 	}	
 
 if(listen(s_socket, 5) == -1) {
  	 printf("listen Fail\n");
  	 return -1;
	 }	

 t_status = pthread_create(&thread, NULL, &thread_main, NULL);
 if (t_status != 0) {
	perror("Error : create failed for thread\n");
 }

 while(1) {
  	 len = sizeof(c_addr);
  	 c_socket = accept(s_socket, (struct sockaddr*) &c_addr, &len);
	 while((n = read(c_socket, rcvBuffer, sizeof(rcvBuffer))) != 0)  {
		 /*if (strlen(rcvBuffer) > 20) {
			char temp[29];
			rcvBuffer[n] = '\0';
			strncpy(temp, rcvBuffer, 29);
			serialPrintf(fd, "%s", temp);
			printf("%d : %s\n", strlen(temp), temp);
		 }*/
                 write(c_socket, rcvBuffer, n);
		 fflush(stdout);
  	    }
	 close(c_socket);
	// serialClose(fd);
 	}
	return 0;
}

void *thread_main(void *arg)
{
	char tmp_gps[64];

        char tmp_bit[4];
	char tmp_link[8];
	char tmp_signal[8];
	char tmp_temp[5];
	char tmp_clock[4];
        char tmp_noise[8];

        FILE *gps;

	FILE *bitrate;
	FILE *link;
	FILE *signal;
	FILE *temp;
	FILE *clock;
        FILE *noise;
    
	while(1) {
                gps = fopen("/home/pi/mjpg/www/gps_log.txt", "r");
            
                bitrate = popen("/sbin/iwconfig wlan0 | grep 'Bit' | cut -d'S' -f 1 | cut -c 20-22", "r");
                link = popen("/sbin/iwconfig wlan0 | grep 'Link Quality' | cut -f 2 -d '=' | cut -f 1 -d 'S'", "r");
		signal = popen("/sbin/iwconfig wlan0 | grep 'Link Quality' | cut -f 3 -d '=' | cut -f 1 -d 'N'", "r");
                noise = popen("/sbin/iwconfig wlan0 | grep 'Link Quality' | cut -f 4 -d '='", "r");
		temp = popen("/opt/vc/bin/vcgencmd measure_temp | cut -d'=' -f 2 | cut -c 1-4", "r");
		clock = popen("/opt/vc/bin/vcgencmd measure_clock arm | cut -d= -f2 | cut -c 1-3", "r");

                fgets(tmp_gps, 64, gps);
		fscanf(bitrate, "%s", tmp_bit);
		fscanf(link, "%s", tmp_link);
		fscanf(signal, "%s", tmp_signal);
		fscanf(noise, "%s", tmp_noise);
                fscanf(temp, "%s", tmp_temp);
		fscanf(clock, "%s", tmp_clock);
        
                if(strlen(tmp_gps) <= 1) {
                    //printf("gps error\n");
                    memset(tmp_gps, 0x00, sizeof(tmp_gps));
                }

                sprintf(info, "@%s@%s@%s@%s@%s@%s@%s@", tmp_bit, tmp_link, tmp_signal, tmp_noise, tmp_temp, tmp_clock, tmp_gps);
		
                printf("%s\n", info);

		if(c_socket) write(c_socket, info, sizeof(info)-1);
                
                fclose(gps);

                pclose(bitrate); 
		pclose(link); 
		pclose(signal); 
		pclose(temp);
		pclose(clock);
                pclose(noise);

		sleep(1);
	}
	pthread_exit((void *) 0);
}
